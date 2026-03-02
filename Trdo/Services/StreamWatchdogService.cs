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
public sealed partial class StreamWatchdogService : IDisposable
{
    private readonly RadioPlayerService _playerService;
    private readonly DispatcherQueue _uiQueue;
    private readonly AudioSilenceMonitorService _silenceMonitor;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private bool _isEnabled;
    private bool _userIntendedPlayback;
    private DateTime _lastStateCheck;
    private int _consecutiveFailures;
    private TimeSpan _lastPosition;
    private DateTime _lastPositionChangeTime;
    private double _lastBufferingProgress;
    private volatile bool _isRecovering;

    // Stutter detection tracking
    private readonly Queue<DateTime> _recoveryAttempts = new();
    private bool _autoBufferIncreaseEnabled;
    private double _currentBufferLevel;
    private const string AutoBufferIncreaseKey = "AutoBufferIncreaseEnabled";
    private const string BufferLevelKey = "BufferLevel";
    private const string SilenceTimeoutKey = "SilenceTimeoutSeconds";
    private const double DefaultSilenceTimeoutSeconds = 5.0;

    // Configuration
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _recoveryDelay = TimeSpan.FromSeconds(3);
    private readonly int _maxConsecutiveFailures = 3;
    private readonly TimeSpan _backoffDelay = TimeSpan.FromSeconds(30);

    // Stutter detection configuration
    private const int StutterThreshold = 3;  // Number of recovery attempts to trigger stutter detection
    private readonly TimeSpan _stutterWindow = TimeSpan.FromMinutes(2);  // Time window to count recovery attempts
    private const double MaxBufferLevel = 3.0;  // Maximum buffer level (0=default, 1=medium, 2=large, 3=extra large)
    private const bool DefaultAutoBufferIncreaseEnabled = true;  // Auto-buffer increase is enabled by default for better user experience
    private const double DefaultBufferLevel = 0.0;  // Start with default (no extra delay) buffer level

    public event EventHandler<StreamWatchdogEventArgs>? StreamStatusChanged;
    public event EventHandler<StutterDetectedEventArgs>? StutterDetected;
    public event EventHandler<double>? BufferLevelChanged;

    /// <summary>
    /// Raised periodically with the current audio output RMS level (0–1 scale).
    /// Forwarded from the NAudio silence monitor for UI visualisation.
    /// Fires on a background thread — marshal to the UI thread before touching XAML.
    /// </summary>
    public event Action<float>? AudioLevelUpdated;

    /// <summary>
    /// Gets or sets the silence detection timeout in seconds.
    /// If the audio output is silent for longer than this while the stream is supposed to be playing,
    /// a recovery attempt is triggered. Persisted to local settings.
    /// </summary>
    public double SilenceTimeoutSeconds
    {
        get => _silenceMonitor.SilenceTimeoutSeconds;
        set
        {
            if (Math.Abs(_silenceMonitor.SilenceTimeoutSeconds - value) < 0.01) return;
            _silenceMonitor.SilenceTimeoutSeconds = value;
            SaveSilenceTimeoutSetting();
            Debug.WriteLine($"[Watchdog] Silence timeout set to: {value}s");
        }
    }

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
    public double BufferLevel
    {
        get => _currentBufferLevel;
        set
        {
            double clampedValue = Math.Clamp(value, 0, MaxBufferLevel);
            if (Math.Abs(_currentBufferLevel - clampedValue) < 0.0001) return;
            double oldValue = _currentBufferLevel;
            _currentBufferLevel = clampedValue;
            SaveAutoBufferSettings();
            Debug.WriteLine($"[Watchdog] Buffer level changed from {oldValue} to {_currentBufferLevel}");
            RaiseBufferLevelChanged(clampedValue);
        }
    }

    /// <summary>
    /// Gets the buffer delay in milliseconds based on current buffer level.
    /// </summary>
    public int BufferDelayMs
    {
        get
        {
            // Linear interpolation between buffer levels
            // Level 0 = 0ms, Level 1 = 2000ms, Level 2 = 4000ms, Level 3 = 8000ms
            if (_currentBufferLevel <= 0) return 0;
            if (_currentBufferLevel >= 3) return 8000;
            if (_currentBufferLevel <= 1) return (int)(_currentBufferLevel * 2000);
            if (_currentBufferLevel <= 2) return (int)(2000 + (_currentBufferLevel - 1) * 2000);
            return (int)(4000 + (_currentBufferLevel - 2) * 4000);
        }
    }

    /// <summary>
    /// Gets a human-readable description of the current buffer level.
    /// </summary>
    public string BufferLevelDescription
    {
        get
        {
            return _currentBufferLevel switch
            {
                0 => "Default",
                1 => "Medium",
                2 => "Large",
                3 => "Extra Large",
                _ when _currentBufferLevel < 0.5 => "Default",
                _ when _currentBufferLevel < 1.5 => "Medium",
                _ when _currentBufferLevel < 2.5 => "Large",
                _ => "Extra Large"
            };
        }
    }

    public StreamWatchdogService(RadioPlayerService playerService)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _lastStateCheck = DateTime.UtcNow;
        _userIntendedPlayback = false;
        _lastPosition = TimeSpan.Zero;
        _lastPositionChangeTime = DateTime.UtcNow;
        _lastBufferingProgress = 0;

        // Initialize NAudio silence monitor
        _silenceMonitor = new AudioSilenceMonitorService();
        _silenceMonitor.SilenceDetected += OnSilenceDetected;
        _silenceMonitor.AudioLevelUpdated += OnAudioLevelFromMonitor;

        // Load settings
        LoadAutoBufferSettings();
        LoadSilenceTimeoutSetting();
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
                    double d => Math.Clamp(d, 0, MaxBufferLevel),
                    int i => Math.Clamp((double)i, 0, MaxBufferLevel),
                    string s when double.TryParse(s, out double d2) => Math.Clamp(d2, 0, MaxBufferLevel),
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

    private void LoadSilenceTimeoutSetting()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SilenceTimeoutKey, out object? value))
            {
                double timeout = value switch
                {
                    double d => d,
                    int i => (double)i,
                    string s when double.TryParse(s, out double d2) => d2,
                    _ => DefaultSilenceTimeoutSeconds
                };
                _silenceMonitor.SilenceTimeoutSeconds = Math.Clamp(timeout, 1.0, 60.0);
            }
            else
            {
                _silenceMonitor.SilenceTimeoutSeconds = DefaultSilenceTimeoutSeconds;
            }
            Debug.WriteLine($"[Watchdog] Loaded silence timeout: {_silenceMonitor.SilenceTimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error loading silence timeout: {ex.Message}");
            _silenceMonitor.SilenceTimeoutSeconds = DefaultSilenceTimeoutSeconds;
        }
    }

    private void SaveSilenceTimeoutSetting()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SilenceTimeoutKey] = _silenceMonitor.SilenceTimeoutSeconds;
            Debug.WriteLine($"[Watchdog] Saved silence timeout: {_silenceMonitor.SilenceTimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error saving silence timeout: {ex.Message}");
        }
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally started playback.
    /// </summary>
    public void NotifyUserIntentionToPlay()
    {
        _userIntendedPlayback = true;
        _consecutiveFailures = 0;
        _lastPositionChangeTime = DateTime.UtcNow;
        StartSilenceMonitor();
        Debug.WriteLine("[Watchdog] User started playback - monitoring active");
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally paused/stopped playback.
    /// </summary>
    public void NotifyUserIntentionToPause()
    {
        _userIntendedPlayback = false;
        _consecutiveFailures = 0;
        StopSilenceMonitor();
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
        _lastPositionChangeTime = DateTime.UtcNow;
        _monitoringTask = Task.Run(() => MonitorStreamAsync(_cts.Token));

        if (_userIntendedPlayback)
            StartSilenceMonitor();

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
        StopSilenceMonitor();
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

            // Stream is playing - NAudio silence monitor handles actual audio detection
            if (!_userIntendedPlayback)
            {
                _userIntendedPlayback = true;
                StartSilenceMonitor();
                Debug.WriteLine("[Watchdog] Stream is playing - silence monitoring active");
            }
            _consecutiveFailures = 0;

            // Log position/buffering for debugging (silence detection is handled by NAudio)
            bool positionChanged = currentPosition != _lastPosition;
            bool bufferingProgressChanged = Math.Abs(currentBufferingProgress - _lastBufferingProgress) > 0.01;

            if (positionChanged || bufferingProgressChanged)
            {
                _lastPosition = currentPosition;
                _lastBufferingProgress = currentBufferingProgress;
                _lastPositionChangeTime = DateTime.UtcNow;
                Debug.WriteLine($"[Watchdog] Stream healthy - Position: {currentPosition}, Buffering: {currentBufferingProgress:P0}");
            }
            else
            {
                TimeSpan stalledDuration = DateTime.UtcNow - _lastPositionChangeTime;
                Debug.WriteLine($"[Watchdog] Position unchanged for {stalledDuration.TotalSeconds:F1}s (NAudio silence monitor active)");
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

            // Wait a bit before attempting recovery
            Debug.WriteLine($"[Watchdog] Waiting {_recoveryDelay.TotalMilliseconds}ms before recovery");
            await Task.Delay(_recoveryDelay, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            // Attempt to restart playback on UI thread
            // Use PlayWithBufferAsync to ensure sufficient buffer is accumulated using GetBufferedRanges
            bool playbackStarted = false;
            Exception? playbackException = null;

            await RunOnUiThreadAsync(() =>
            {
                try
                {
                    string? streamUrl = _playerService.StreamUrl;
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        // Reinitialize the stream
                        _playerService.SetStreamUrl(streamUrl);
                        Debug.WriteLine("[Watchdog] Stream URL set, starting buffered playback");
                    }
                }
                catch (Exception ex)
                {
                    playbackException = ex;
                    Debug.WriteLine($"[Watchdog] Failed to set stream URL: {ex.Message}");
                }
            });

            if (playbackException != null)
            {
                RaiseStatusChanged($"Recovery failed: {playbackException.Message}", StreamWatchdogStatus.Error);
                return;
            }

            // Use PlayWithBufferAsync to wait for sufficient buffer based on GetBufferedRanges
            // This ensures smooth playback by checking buffered content before audio starts
            Debug.WriteLine($"[Watchdog] Starting playback with buffer monitoring (required: {_playerService.RequiredBufferDuration.TotalMilliseconds}ms)");
            playbackStarted = await _playerService.PlayWithBufferAsync(cancellationToken);

            if (playbackStarted)
            {
                Debug.WriteLine($"[Watchdog] Stream recovery successful with buffer: {_playerService.TotalBufferedDuration.TotalMilliseconds}ms");
                RaiseStatusChanged("Stream resumed with buffer", StreamWatchdogStatus.Recovering);
            }
            else
            {
                Debug.WriteLine("[Watchdog] Playback start was cancelled");
                RaiseStatusChanged("Recovery cancelled", StreamWatchdogStatus.Error);
            }
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

            double previousLevel = _currentBufferLevel;

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
                BufferWasIncreased = Math.Abs(previousLevel - _currentBufferLevel) > 0.0001
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

    /// <summary>
    /// Starts the NAudio silence monitor if the watchdog is enabled and user intends playback.
    /// </summary>
    private void StartSilenceMonitor()
    {
        if (_isEnabled && _userIntendedPlayback)
        {
            _silenceMonitor.Start();
        }
    }

    /// <summary>
    /// Stops the NAudio silence monitor.
    /// </summary>
    private void StopSilenceMonitor()
    {
        _silenceMonitor.Stop();
    }

    private void OnAudioLevelFromMonitor(float level) => AudioLevelUpdated?.Invoke(level);

    /// <summary>
    /// Called by the NAudio silence monitor when audio output has been silent
    /// for longer than <see cref="SilenceTimeoutSeconds"/>.
    /// </summary>
    private async void OnSilenceDetected(object? sender, EventArgs e)
    {
        if (!_isEnabled || !_userIntendedPlayback || _isRecovering)
            return;

        try
        {
            _isRecovering = true;
            StopSilenceMonitor();

            Debug.WriteLine("[Watchdog] NAudio silence detected - attempting stream recovery");
            RaiseStatusChanged("Stream is silent - refreshing", StreamWatchdogStatus.Recovering);

            CancellationToken token = _cts?.Token ?? CancellationToken.None;
            await AttemptRecoveryAsync(token);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Watchdog] Silence recovery cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Watchdog] Error during silence recovery: {ex.Message}");
            RaiseStatusChanged($"Recovery error: {ex.Message}", StreamWatchdogStatus.Error);
        }
        finally
        {
            _isRecovering = false;
            StartSilenceMonitor();
        }
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

    private void RaiseBufferLevelChanged(double newLevel)
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
        _silenceMonitor.SilenceDetected -= OnSilenceDetected;
        _silenceMonitor.AudioLevelUpdated -= OnAudioLevelFromMonitor;
        _silenceMonitor.Dispose();
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
    public double PreviousBufferLevel { get; init; }

    /// <summary>
    /// The buffer level after auto-increase was applied (may be same as previous if at max or disabled).
    /// </summary>
    public double NewBufferLevel { get; init; }

    /// <summary>
    /// Whether the buffer level was actually increased.
    /// </summary>
    public bool BufferWasIncreased { get; init; }

    /// <summary>
    /// Timestamp when the stutter was detected.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
