using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Trdo.Services;

/// <summary>
/// Monitors the radio stream and automatically resumes playback when the stream stops unexpectedly.
/// Also tracks stutter patterns and can automatically increase buffer when stuttering is detected.
/// </summary>
public sealed class StreamWatchdogService : IDisposable
{
    private readonly RadioPlayerService _playerService;
    private readonly DispatcherQueue _uiQueue;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private bool _isEnabled;
    private bool _userIntendedPlayback;
    private DateTime _lastStateCheck;
    private int _consecutiveFailures;
    private TimeSpan _lastPosition;
    private DateTime _lastPositionChangeTime;
    private double _lastBufferingProgress;
    private int _consecutiveSilentChecks;

    // Stutter detection tracking
    private readonly Queue<DateTime> _recoveryAttempts = new();
    private bool _autoBufferIncreaseEnabled;
    private int _currentBufferLevel;
    private const string AutoBufferIncreaseKey = "AutoBufferIncreaseEnabled";
    private const string BufferLevelKey = "BufferLevel";

    // Configuration
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _recoveryDelay = TimeSpan.FromSeconds(3);
    private readonly int _maxConsecutiveFailures = 3;
    private readonly TimeSpan _backoffDelay = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _silenceDetectionThreshold = TimeSpan.FromSeconds(10);
    private readonly int _maxConsecutiveSilentChecks = 2; // 2 checks * 5 seconds = 10 seconds

    // Stutter detection configuration
    private const int StutterThreshold = 3;  // Number of recovery attempts to trigger stutter detection
    private readonly TimeSpan _stutterWindow = TimeSpan.FromMinutes(2);  // Time window to count recovery attempts
    private const int MaxBufferLevel = 3;  // Maximum buffer level (0=default, 1=medium, 2=large, 3=extra large)
    private const bool DefaultAutoBufferIncreaseEnabled = true;  // Auto-buffer increase is enabled by default for better user experience
    private const int DefaultBufferLevel = 0;  // Start with default (no extra delay) buffer level

    public event EventHandler<StreamWatchdogEventArgs>? StreamStatusChanged;
    public event EventHandler<StutterDetectedEventArgs>? StutterDetected;
    public event EventHandler<int>? BufferLevelChanged;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;

            if (_isEnabled)
                Start();
            else
                Stop();
        }
    }

    /// <summary>
    /// Gets or sets whether auto-buffer increase is enabled.
    /// When enabled, the buffer level automatically increases when stutter is detected.
    /// </summary>
    public bool AutoBufferIncreaseEnabled
    {
        get => _autoBufferIncreaseEnabled;
        set
        {
            if (_autoBufferIncreaseEnabled == value) return;
            _autoBufferIncreaseEnabled = value;
            SaveAutoBufferSettings();
            Debug.WriteLine($"[Watchdog] Auto-buffer increase set to: {value}");
        }
    }

    /// <summary>
    /// Gets or sets the current buffer level (0-3).
    /// 0 = Default, 1 = Medium, 2 = Large, 3 = Extra Large
    /// </summary>
    public int BufferLevel
    {
        get => _currentBufferLevel;
        set
        {
            int clampedValue = Math.Clamp(value, 0, MaxBufferLevel);
            if (_currentBufferLevel == clampedValue) return;
            int oldValue = _currentBufferLevel;
            _currentBufferLevel = clampedValue;
            SaveAutoBufferSettings();
            Debug.WriteLine($"[Watchdog] Buffer level changed from {oldValue} to {_currentBufferLevel}");
            RaiseBufferLevelChanged(clampedValue);
        }
    }

    /// <summary>
    /// Gets the buffer delay in milliseconds based on current buffer level.
    /// </summary>
    public int BufferDelayMs => _currentBufferLevel switch
    {
        0 => 0,      // Default - no additional delay
        1 => 2000,   // Medium - 2 second buffer
        2 => 4000,   // Large - 4 second buffer
        3 => 8000,   // Extra Large - 8 second buffer
        _ => 0
    };

    /// <summary>
    /// Gets a human-readable description of the current buffer level.
    /// </summary>
    public string BufferLevelDescription => _currentBufferLevel switch
    {
        0 => "Default",
        1 => "Medium",
        2 => "Large",
        3 => "Extra Large",
        _ => "Default"
    };

    public StreamWatchdogService(RadioPlayerService playerService)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _lastStateCheck = DateTime.UtcNow;
        _userIntendedPlayback = false;
        _lastPosition = TimeSpan.Zero;
        _lastPositionChangeTime = DateTime.UtcNow;
        _lastBufferingProgress = 0;
        _consecutiveSilentChecks = 0;

        // Load auto-buffer settings
        LoadAutoBufferSettings();
    }

    private void LoadAutoBufferSettings()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(AutoBufferIncreaseKey, out object? autoBufferValue))
            {
                _autoBufferIncreaseEnabled = autoBufferValue switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out bool b2) => b2,
                    _ => DefaultAutoBufferIncreaseEnabled
                };
            }
            else
            {
                _autoBufferIncreaseEnabled = DefaultAutoBufferIncreaseEnabled;
            }

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(BufferLevelKey, out object? bufferLevelValue))
            {
                _currentBufferLevel = bufferLevelValue switch
                {
                    int i => Math.Clamp(i, 0, MaxBufferLevel),
                    string s when int.TryParse(s, out int i2) => Math.Clamp(i2, 0, MaxBufferLevel),
                    _ => DefaultBufferLevel
                };
            }
            else
            {
                _currentBufferLevel = DefaultBufferLevel;
            }

            Debug.WriteLine($"[Watchdog] Loaded settings - AutoBufferIncrease: {_autoBufferIncreaseEnabled}, BufferLevel: {_currentBufferLevel}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error loading auto-buffer settings: {ex.Message}");
            _autoBufferIncreaseEnabled = DefaultAutoBufferIncreaseEnabled;
            _currentBufferLevel = DefaultBufferLevel;
        }
    }

    private void SaveAutoBufferSettings()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[AutoBufferIncreaseKey] = _autoBufferIncreaseEnabled;
            ApplicationData.Current.LocalSettings.Values[BufferLevelKey] = _currentBufferLevel;
            Debug.WriteLine($"[Watchdog] Saved settings - AutoBufferIncrease: {_autoBufferIncreaseEnabled}, BufferLevel: {_currentBufferLevel}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error saving auto-buffer settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally started playback.
    /// </summary>
    public void NotifyUserIntentionToPlay()
    {
        _userIntendedPlayback = true;
        _consecutiveFailures = 0;
        _consecutiveSilentChecks = 0;
        _lastPositionChangeTime = DateTime.UtcNow;
        Debug.WriteLine("[Watchdog] User started playback - monitoring active");
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally paused/stopped playback.
    /// </summary>
    public void NotifyUserIntentionToPause()
    {
        _userIntendedPlayback = false;
        _consecutiveFailures = 0;
        _consecutiveSilentChecks = 0;
        Debug.WriteLine("[Watchdog] User paused playback - recovery disabled");
    }

    /// <summary>
    /// Starts monitoring the stream.
    /// </summary>
    public void Start()
    {
        if (_monitoringTask is not null && !_monitoringTask.IsCompleted)
            return; // Already running

        _cts = new CancellationTokenSource();
        _consecutiveFailures = 0;
        _consecutiveSilentChecks = 0;
        _lastPositionChangeTime = DateTime.UtcNow;
        _monitoringTask = Task.Run(() => MonitorStreamAsync(_cts.Token));

        RaiseStatusChanged("Watchdog started", StreamWatchdogStatus.Monitoring);
    }

    /// <summary>
    /// Stops monitoring the stream.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _monitoringTask = null;
        _userIntendedPlayback = false;
        RaiseStatusChanged("Watchdog stopped", StreamWatchdogStatus.Stopped);
    }

    private async Task MonitorStreamAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine("[Watchdog] Monitoring started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    break;

                await CheckStreamHealthAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watchdog] Error in monitoring loop: {ex.Message}");
                RaiseStatusChanged($"Monitoring error: {ex.Message}", StreamWatchdogStatus.Error);
            }
        }

        Debug.WriteLine("[Watchdog] Monitoring stopped");
    }

    private async Task CheckStreamHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool isPlaying = false;
            TimeSpan currentPosition = TimeSpan.Zero;
            double currentBufferingProgress = 0;

            // Get current state on UI thread
            await RunOnUiThreadAsync(() =>
            {
                try
                {
                    isPlaying = _playerService.IsPlaying;
                    currentPosition = _playerService.Position;
                    currentBufferingProgress = _playerService.BufferingProgress;
                }
                catch
                {
                    // Player might be disposed or in invalid state
                }
            });

            // If stream is not playing, handle it as before
            if (!isPlaying)
            {
                // Only attempt recovery if user intended to have it playing
                if (!_userIntendedPlayback)
                {
                    // User intentionally stopped/paused - don't attempt recovery
                    return;
                }

                // Stream stopped unexpectedly - attempt recovery
                TimeSpan timeSinceLastCheck = DateTime.UtcNow - _lastStateCheck;

                // Only attempt recovery if enough time has passed
                if (timeSinceLastCheck > _checkInterval)
                {
                    _consecutiveFailures++;
                    _consecutiveSilentChecks = 0; // Reset silent checks when not playing
                    Debug.WriteLine($"[Watchdog] Stream stopped unexpectedly. Attempt {_consecutiveFailures}/{_maxConsecutiveFailures}");

                    RaiseStatusChanged($"Stream stopped. Recovery attempt {_consecutiveFailures}/{_maxConsecutiveFailures}",
                        StreamWatchdogStatus.Recovering);

                    if (_consecutiveFailures <= _maxConsecutiveFailures)
                    {
                        await AttemptRecoveryAsync(cancellationToken);
                    }
                    else
                    {
                        Debug.WriteLine("[Watchdog] Max recovery attempts reached. Backing off.");
                        RaiseStatusChanged("Max recovery attempts reached. Will retry later.",
                            StreamWatchdogStatus.BackingOff);

                        // Wait longer before next attempt
                        await Task.Delay(_backoffDelay, cancellationToken);
                        _consecutiveFailures = 0; // Reset after backoff
                    }
                }

                _lastStateCheck = DateTime.UtcNow;
                return;
            }

            // Stream is playing - check if audio is actually progressing
            // Don't overwrite user intention if they manually started
            if (!_userIntendedPlayback)
            {
                _userIntendedPlayback = true;
                Debug.WriteLine("[Watchdog] Stream is playing - monitoring active");
            }
            _consecutiveFailures = 0;

            // Check if position or buffering progress has changed
            bool positionChanged = currentPosition != _lastPosition;
            bool bufferingProgressChanged = Math.Abs(currentBufferingProgress - _lastBufferingProgress) > 0.01;

            if (positionChanged || bufferingProgressChanged)
            {
                // Stream is healthy - audio is progressing
                _lastPosition = currentPosition;
                _lastBufferingProgress = currentBufferingProgress;
                _lastPositionChangeTime = DateTime.UtcNow;
                _consecutiveSilentChecks = 0;
                Debug.WriteLine($"[Watchdog] Stream healthy - Position: {currentPosition}, Buffering: {currentBufferingProgress:P0}");
            }
            else
            {
                // Position hasn't changed - potential silent stream
                TimeSpan silenceDuration = DateTime.UtcNow - _lastPositionChangeTime;
                
                if (silenceDuration > _silenceDetectionThreshold)
                {
                    _consecutiveSilentChecks++;
                    Debug.WriteLine($"[Watchdog] Silent stream detected for {silenceDuration.TotalSeconds:F1}s. Check {_consecutiveSilentChecks}/{_maxConsecutiveSilentChecks}");

                    if (_consecutiveSilentChecks >= _maxConsecutiveSilentChecks)
                    {
                        // Stream is playing but no audio for too long - attempt recovery
                        Debug.WriteLine("[Watchdog] Stream is silent - attempting recovery");
                        RaiseStatusChanged("Stream is silent - refreshing", StreamWatchdogStatus.Recovering);
                        
                        _consecutiveSilentChecks = 0;
                        _lastPositionChangeTime = DateTime.UtcNow;
                        
                        await AttemptRecoveryAsync(cancellationToken);
                    }
                }
                else
                {
                    // Within threshold, keep monitoring
                    Debug.WriteLine($"[Watchdog] Position unchanged for {silenceDuration.TotalSeconds:F1}s (threshold: {_silenceDetectionThreshold.TotalSeconds}s)");
                }
            }

            _lastStateCheck = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error checking stream health: {ex.Message}");
        }
    }

    private async Task AttemptRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            Debug.WriteLine("[Watchdog] Attempting to resume stream...");

            // Track this recovery attempt for stutter detection
            TrackRecoveryAttempt();

            // Wait a bit before attempting recovery, adding buffer delay
            int totalDelay = (int)_recoveryDelay.TotalMilliseconds + BufferDelayMs;
            Debug.WriteLine($"[Watchdog] Waiting {totalDelay}ms before recovery (base: {_recoveryDelay.TotalMilliseconds}ms, buffer: {BufferDelayMs}ms)");
            await Task.Delay(totalDelay, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            // Attempt to restart playback on UI thread
            await RunOnUiThreadAsync(() =>
            {
                try
                {
                    string? streamUrl = _playerService.StreamUrl;
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        // Reinitialize the stream
                        _playerService.SetStreamUrl(streamUrl);

                        // Resume playback
                        _playerService.Play();

                        Debug.WriteLine("[Watchdog] Stream recovery initiated");
                        RaiseStatusChanged("Stream resumed", StreamWatchdogStatus.Recovering);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Watchdog] Failed to resume stream: {ex.Message}");
                    RaiseStatusChanged($"Recovery failed: {ex.Message}", StreamWatchdogStatus.Error);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation during recovery
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error during recovery: {ex.Message}");
            RaiseStatusChanged($"Recovery error: {ex.Message}", StreamWatchdogStatus.Error);
        }
    }

    /// <summary>
    /// Tracks a recovery attempt and checks for stutter pattern.
    /// If stuttering is detected and auto-buffer increase is enabled, increases the buffer level.
    /// </summary>
    private void TrackRecoveryAttempt()
    {
        DateTime now = DateTime.UtcNow;
        _recoveryAttempts.Enqueue(now);

        // Remove old attempts outside the stutter window
        while (_recoveryAttempts.Count > 0 &&
               now - _recoveryAttempts.Peek() > _stutterWindow)
        {
            _recoveryAttempts.Dequeue();
        }

        Debug.WriteLine($"[Watchdog] Recovery attempts in last {_stutterWindow.TotalMinutes}min: {_recoveryAttempts.Count}");

        // Check if we've hit the stutter threshold
        if (_recoveryAttempts.Count >= StutterThreshold)
        {
            int recoveryCount = _recoveryAttempts.Count;
            Debug.WriteLine($"[Watchdog] STUTTER DETECTED - {recoveryCount} recoveries in {_stutterWindow.TotalMinutes}min window");

            int previousLevel = _currentBufferLevel;

            // Auto-increase buffer if enabled and not at max
            if (_autoBufferIncreaseEnabled && _currentBufferLevel < MaxBufferLevel)
            {
                BufferLevel = _currentBufferLevel + 1;
                Debug.WriteLine($"[Watchdog] Auto-increased buffer level from {previousLevel} to {_currentBufferLevel} ({BufferLevelDescription})");

                // Clear recovery attempts after buffer increase to give the new level a chance
                _recoveryAttempts.Clear();
            }

            // Raise the stutter detected event
            RaiseStutterDetected(new StutterDetectedEventArgs
            {
                RecoveryAttemptCount = recoveryCount,
                TimeWindow = _stutterWindow,
                PreviousBufferLevel = previousLevel,
                NewBufferLevel = _currentBufferLevel,
                BufferWasIncreased = previousLevel != _currentBufferLevel
            });
        }
    }

    /// <summary>
    /// Resets the buffer level to default. Call this when the user manually changes stations or wants to reset.
    /// </summary>
    public void ResetBufferLevel()
    {
        _recoveryAttempts.Clear();
        BufferLevel = 0;
        Debug.WriteLine("[Watchdog] Buffer level reset to default");
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        TaskCompletionSource<bool> tcs = new();

        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }
        else
        {
            _uiQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }

        return tcs.Task;
    }

    private void RaiseStatusChanged(string message, StreamWatchdogStatus status)
    {
        Debug.WriteLine($"[Watchdog] {status}: {message}");

        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            StreamStatusChanged?.Invoke(this, new StreamWatchdogEventArgs(message, status));
        }
        else
        {
            _uiQueue.TryEnqueue(() =>
            {
                StreamStatusChanged?.Invoke(this, new StreamWatchdogEventArgs(message, status));
            });
        }
    }

    private void RaiseStutterDetected(StutterDetectedEventArgs args)
    {
        Debug.WriteLine($"[Watchdog] Raising StutterDetected event - BufferIncreased: {args.BufferWasIncreased}, NewLevel: {args.NewBufferLevel}");

        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            StutterDetected?.Invoke(this, args);
        }
        else
        {
            _uiQueue.TryEnqueue(() =>
            {
                StutterDetected?.Invoke(this, args);
            });
        }
    }

    private void RaiseBufferLevelChanged(int newLevel)
    {
        Debug.WriteLine($"[Watchdog] Raising BufferLevelChanged event - NewLevel: {newLevel}");

        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            BufferLevelChanged?.Invoke(this, newLevel);
        }
        else
        {
            _uiQueue.TryEnqueue(() =>
            {
                BufferLevelChanged?.Invoke(this, newLevel);
            });
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

/// <summary>
/// Event arguments for stream watchdog status changes.
/// </summary>
public class StreamWatchdogEventArgs : EventArgs
{
    public string Message { get; }
    public StreamWatchdogStatus Status { get; }
    public DateTime Timestamp { get; }

    public StreamWatchdogEventArgs(string message, StreamWatchdogStatus status)
    {
        Message = message;
        Status = status;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Status of the stream watchdog.
/// </summary>
public enum StreamWatchdogStatus
{
    Stopped,
    Monitoring,
    Recovering,
    BackingOff,
    Error
}

/// <summary>
/// Event arguments for stutter detection events.
/// </summary>
public class StutterDetectedEventArgs : EventArgs
{
    /// <summary>
    /// Number of recovery attempts within the time window that triggered stutter detection.
    /// </summary>
    public int RecoveryAttemptCount { get; init; }

    /// <summary>
    /// The time window used for stutter detection.
    /// </summary>
    public TimeSpan TimeWindow { get; init; }

    /// <summary>
    /// The buffer level before auto-increase was applied.
    /// </summary>
    public int PreviousBufferLevel { get; init; }

    /// <summary>
    /// The buffer level after auto-increase was applied (may be same as previous if at max or disabled).
    /// </summary>
    public int NewBufferLevel { get; init; }

    /// <summary>
    /// Whether the buffer level was actually increased.
    /// </summary>
    public bool BufferWasIncreased { get; init; }

    /// <summary>
    /// Timestamp when the stutter was detected.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
