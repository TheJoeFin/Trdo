using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace Tradio.Services;

/// <summary>
/// Monitors the radio stream and automatically resumes playback when the stream stops unexpectedly.
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

    // Configuration
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _recoveryDelay = TimeSpan.FromSeconds(3);
    private readonly int _maxConsecutiveFailures = 3;
    private readonly TimeSpan _backoffDelay = TimeSpan.FromSeconds(30);

    public event EventHandler<StreamWatchdogEventArgs>? StreamStatusChanged;

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

    public StreamWatchdogService(RadioPlayerService playerService)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _lastStateCheck = DateTime.UtcNow;
        _userIntendedPlayback = false;
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally started playback.
    /// </summary>
    public void NotifyUserIntentionToPlay()
    {
        _userIntendedPlayback = true;
        _consecutiveFailures = 0;
        Debug.WriteLine("[Watchdog] User started playback - monitoring active");
    }

    /// <summary>
    /// Notify the watchdog that the user intentionally paused/stopped playback.
    /// </summary>
    public void NotifyUserIntentionToPause()
    {
        _userIntendedPlayback = false;
        _consecutiveFailures = 0;
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

            // Get current state on UI thread
            await RunOnUiThreadAsync(() =>
            {
                try
                {
                    isPlaying = _playerService.IsPlaying;
                }
                catch
                {
                    // Player might be disposed or in invalid state
                }
            });

            // If stream is playing, update state and return
            if (isPlaying)
            {
                // Don't overwrite user intention if they manually started
                if (!_userIntendedPlayback)
                {
                    _userIntendedPlayback = true;
                    Debug.WriteLine("[Watchdog] Stream is playing - monitoring active");
                }
                _consecutiveFailures = 0;
                _lastStateCheck = DateTime.UtcNow;
                return; // Stream is healthy
            }

            // If we get here, the stream is not playing
            // Only attempt recovery if user intended to have it playing
            if (!_userIntendedPlayback)
            {
                // User intentionally stopped/paused - don't attempt recovery
                return;
            }

            // Stream stopped unexpectedly - attempt recovery
            var timeSinceLastCheck = DateTime.UtcNow - _lastStateCheck;
            
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

            // Wait a bit before attempting recovery
            await Task.Delay(_recoveryDelay, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            // Attempt to restart playback on UI thread
            await RunOnUiThreadAsync(() =>
            {
                try
                {
                    var streamUrl = _playerService.StreamUrl;
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

    private Task RunOnUiThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();

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
