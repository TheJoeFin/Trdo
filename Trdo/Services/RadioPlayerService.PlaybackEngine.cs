using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Services.Metadata;
using Trdo.Services.Playback;

namespace Trdo.Services;

public sealed partial class RadioPlayerService
{
    private StreamMetadataService _icyMetadataService = null!;
    private NativeTimedMetadataService _nativeTimedMetadataService = null!;
    private LibVlcMetadataProvider _libVlcMetadataProvider = null!;
    private HlsSegmentMetadataService _hlsSegmentMetadataService = null!;
    private StreamMetadataOrchestrator _metadataOrchestrator = null!;
    private NativePlaybackBackend _nativeBackend = null!;
    private LibVlcPlaybackBackend? _libVlcBackend;
    private PlaybackEngineSelector _playbackEngineSelector = null!;
    private string? _lastPrepareError;

    public string? LastPrepareError => _lastPrepareError;

    public PlaybackBackendKind ActivePlaybackBackend => _playbackEngineSelector.ActiveBackendKind;

    private IPlaybackBackend ActiveBackend => _playbackEngineSelector.ActiveBackend;

    private void InitializePlaybackEngine()
    {
        _icyMetadataService = new StreamMetadataService();
        _nativeTimedMetadataService = new NativeTimedMetadataService(_uiQueue);
        _libVlcMetadataProvider = new LibVlcMetadataProvider();
        _hlsSegmentMetadataService = new HlsSegmentMetadataService();
        _metadataOrchestrator = new StreamMetadataOrchestrator(
            _icyMetadataService,
            _nativeTimedMetadataService,
            _libVlcMetadataProvider,
            _hlsSegmentMetadataService);

        _metadataOrchestrator.MetadataChanged += (_, metadata) =>
        {
            Debug.WriteLine($"[RadioPlayerService] Metadata changed: {metadata.DisplayText}");
            TryEnqueueOnUi(() =>
            {
                StreamMetadataChanged?.Invoke(this, metadata);
                ScheduleSystemMediaTransportControlsUpdate();
            });
        };

        _nativeBackend = new NativePlaybackBackend(_player, _httpClient);
        _nativeBackend.PlaybackFailed += OnNativePlaybackFailed;

        if (LibVlcHost.TryInitialize() && LibVlcHost.Instance is not null)
        {
            _libVlcBackend = new LibVlcPlaybackBackend(LibVlcHost.Instance);
            _libVlcBackend.PlaybackStateChanged += OnLibVlcPlaybackStateChanged;
            _libVlcBackend.BufferingStateChanged += OnLibVlcBackendBufferingStateChanged;
            _libVlcBackend.PlaybackFailed += OnLibVlcPlaybackFailed;
        }

        _playbackEngineSelector = new PlaybackEngineSelector(_nativeBackend, _libVlcBackend);
        Debug.WriteLine("[RadioPlayerService] Playback engine initialized");
    }

    private async Task<PlaybackPrepareResult> PrepareStreamAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            throw new InvalidOperationException("No stream URL set. Call SetStreamUrl first.");
        }

        if (_libVlcBackend is not null)
        {
            _libVlcBackend.NetworkCachingMs = (int)RequiredBufferDuration.TotalMilliseconds;
        }

        // Report the value LibVLC will actually use - a configured 0 becomes its own default.
        int cachingMs = _libVlcBackend?.EffectiveNetworkCachingMs ?? (int)RequiredBufferDuration.TotalMilliseconds;
        LogService.Info("RadioPlayerService",
            $"Preparing stream {LogService.Redact(_streamUrl)} (networkCaching={cachingMs}ms)");

        PlaybackPrepareResult result = await _playbackEngineSelector.PrepareAsync(_streamUrl, cancellationToken);
        _lastPrepareError = result.Success ? null : result.ErrorMessage;

        LogService.Info("RadioPlayerService",
            $"Prepare result: success={result.Success}, backend={result.Backend}, usedFallback={result.UsedFallback}" +
            (result.Success ? string.Empty : $", error={result.ErrorMessage}"));

        if (!result.Success)
        {
            return result;
        }

        if (result.Backend == PlaybackBackendKind.Native &&
            _libVlcBackend is not null &&
            PlaybackEngineSelector.GetEngineMode() == PlaybackEngineMode.NativePreferred)
        {
            bool opened = await _playbackEngineSelector.WaitForNativeOpenAsync(
                cancellationToken,
                TimeSpan.FromSeconds(15));

            if (!opened)
            {
                LogService.Warn("RadioPlayerService", "Native open timed out after 15s; attempting LibVLC fallback");
                Debug.WriteLine("[RadioPlayerService] Native open timeout, attempting LibVLC fallback");
                result = await _playbackEngineSelector.RetryWithFallbackAsync(_streamUrl, cancellationToken);
                _lastPrepareError = result.Success ? null : result.ErrorMessage;
            }
        }

        SyncActiveBackendVolume();
        return result;
    }

    public Task<bool> RetryWithPlaybackFallbackAsync(CancellationToken cancellationToken = default)
    {
        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            return RetryWithPlaybackFallbackInternalAsync(cancellationToken);
        }

        TaskCompletionSource<bool> tcs = new();
        _uiQueue.TryEnqueue(async () =>
        {
            try
            {
                bool result = await RetryWithPlaybackFallbackInternalAsync(cancellationToken);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private async Task<bool> RetryWithPlaybackFallbackInternalAsync(CancellationToken cancellationToken = default)
    {
        // Retrying from LibVLC to native needs no LibVLC backend; only the
        // native-to-LibVLC direction requires one.
        if (string.IsNullOrWhiteSpace(_streamUrl) ||
            (_libVlcBackend is null && ActivePlaybackBackend == PlaybackBackendKind.Native))
        {
            return false;
        }

        PlaybackPrepareResult result = await _playbackEngineSelector.RetryWithFallbackAsync(_streamUrl, cancellationToken);
        _lastPrepareError = result.Success ? null : result.ErrorMessage;
        if (!result.Success)
        {
            return false;
        }

        SyncActiveBackendVolume();
        return true;
    }

    /// <summary>
    /// Tears the playback pipeline down and rebuilds it from scratch for the current stream.
    /// This is the "hard reset" rung of the recovery ladder: unlike a soft retry, it discards
    /// the backend's player state, not just the media, so a poisoned pipeline can't survive.
    /// </summary>
    /// <param name="recycleBackend">
    /// When true, the active backend's underlying player object is recreated as well.
    /// </param>
    /// <returns>True if the rebuilt pipeline reached confirmed playback.</returns>
    public Task<bool> RebuildPlaybackPipelineAsync(bool recycleBackend, CancellationToken cancellationToken = default)
    {
        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            return RebuildPlaybackPipelineInternalAsync(recycleBackend, cancellationToken);
        }

        TaskCompletionSource<bool> tcs = new();
        _uiQueue.TryEnqueue(async () =>
        {
            try
            {
                bool result = await RebuildPlaybackPipelineInternalAsync(recycleBackend, cancellationToken);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private async Task<bool> RebuildPlaybackPipelineInternalAsync(bool recycleBackend, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return false;
        }

        LogService.Warn("RadioPlayerService",
            $"Rebuilding playback pipeline for {LogService.Redact(_streamUrl)} " +
            $"(backend={ActivePlaybackBackend}, recycleBackend={recycleBackend})");

        try
        {
            SetInternalStateChange(true);
            ActiveBackend.Pause();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Error pausing during rebuild: {ex.Message}");
        }

        StopMetadata();
        ClearActiveBackendSource();

        if (recycleBackend)
        {
            RecycleActiveBackend();
        }

        // A rebuilt pipeline has no source and is not resuming from a user pause, so both
        // flags must return to their first-play values or the prepare below would be skipped.
        _hasPlayedOnce = false;
        _wasExternalPause = false;

        PlaybackPrepareResult result = await PrepareStreamAsync(cancellationToken);
        if (!result.Success)
        {
            LogService.Error("RadioPlayerService", $"Rebuild prepare failed: {result.ErrorMessage}");
            SetManualBuffering(false);
            return false;
        }

        SyncActiveBackendVolume();

        bool started = await PlayWithBufferAsync(cancellationToken);
        LogService.Info("RadioPlayerService",
            $"Pipeline rebuild {(started ? "succeeded" : "did not reach playback")} on {ActivePlaybackBackend}");

        return started;
    }

    /// <summary>
    /// Recreates the underlying player object of the active backend, discarding any
    /// internal state the previous one accumulated.
    /// </summary>
    private void RecycleActiveBackend()
    {
        try
        {
            if (ActivePlaybackBackend == PlaybackBackendKind.LibVlc && _libVlcBackend is not null)
            {
                _libVlcBackend.Recycle();
            }
            else
            {
                // The native backend rides on the shared WinRT MediaPlayer, which cannot be
                // recreated without rebuilding SMTC and every subscriber. Dropping the source
                // is the equivalent teardown for it.
                _player.Source = null;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("RadioPlayerService", $"Backend recycle failed: {ex.Message}");
            Debug.WriteLine($"[RadioPlayerService] Backend recycle failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks the active backend as unable to play the current stream, so the next prepare
    /// starts with the other one, then rebuilds the pipeline.
    /// </summary>
    /// <returns>True if the rebuilt pipeline reached confirmed playback.</returns>
    public async Task<bool> SwitchBackendAndRebuildAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return false;
        }

        LogService.Warn("RadioPlayerService",
            $"Marking {ActivePlaybackBackend} unhealthy for {LogService.Redact(_streamUrl)} and switching backend");
        _playbackEngineSelector.MarkBackendUnhealthy(_streamUrl, ActivePlaybackBackend);

        return await RebuildPlaybackPipelineAsync(recycleBackend: true, cancellationToken);
    }

    private void StartMetadataForActiveBackend()
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return;
        }

        _metadataOrchestrator.EnsureForPlayback(
            _streamUrl,
            _playbackEngineSelector.ActiveBackendKind,
            _nativeBackend.CurrentPlaybackItem,
            _libVlcBackend?.VlcMediaPlayer);
    }

    private void StopMetadata()
    {
        _metadataOrchestrator.StopAll();
    }

    public async Task RefreshMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return;
        }

        await _metadataOrchestrator.RefreshAsync(
            _streamUrl,
            _playbackEngineSelector.ActiveBackendKind,
            _nativeBackend.CurrentPlaybackItem,
            _libVlcBackend?.VlcMediaPlayer,
            cancellationToken);
    }

    /// <summary>
    /// Records the active backend as proven for the current stream. Called from the
    /// backends' playing transitions - the only reliable evidence a backend works here.
    /// </summary>
    private void ConfirmActiveBackendHealthy()
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return;
        }

        _playbackEngineSelector.ConfirmBackendHealthy(_streamUrl, ActivePlaybackBackend);
    }

    private void SyncActiveBackendVolume()
    {
        SetActiveBackendVolume(GetActiveBackendTargetVolume());
    }

    private double GetActiveBackendTargetVolume()
    {
        return ActivePlaybackBackend == PlaybackBackendKind.Native
            ? Math.Min(_volume, 1)
            : _volume;
    }

    private void SetActiveBackendVolume(double volume)
    {
        double maximum = ActivePlaybackBackend == PlaybackBackendKind.Native ? 1 : 2;
        _activeBackendVolume = Math.Clamp(volume, 0, maximum);
        ActiveBackend.SetVolume(_activeBackendVolume);
    }

    private async Task PlayActiveBackendWithFadeInAsync(CancellationToken cancellationToken)
    {
        SetActiveBackendVolume(0);
        SetInternalStateChange(true);
        ActiveBackend.Play();

        await FadeActiveBackendVolumeAsync(
            targetVolume: GetActiveBackendTargetVolume(),
            FadeInDuration,
            followUserVolume: true,
            cancellationToken);
    }

    private async Task FadeActiveBackendVolumeAsync(
        double targetVolume,
        TimeSpan duration,
        bool followUserVolume,
        CancellationToken cancellationToken)
    {
        await _volumeFadeLock.WaitAsync(cancellationToken);
        try
        {
            _isVolumeFading = true;
            double startVolume = _activeBackendVolume;
            int steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / FadeStepInterval.TotalMilliseconds));

            for (int step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double progress = (double)step / steps;
                double easedProgress = progress * progress * (3 - (2 * progress));
                double currentTarget = followUserVolume
                    ? GetActiveBackendTargetVolume()
                    : targetVolume;

                SetActiveBackendVolume(startVolume + ((currentTarget - startVolume) * easedProgress));

                if (step < steps)
                {
                    await Task.Delay(FadeStepInterval, cancellationToken);
                }
            }

            SetActiveBackendVolume(followUserVolume ? GetActiveBackendTargetVolume() : targetVolume);
        }
        finally
        {
            _isVolumeFading = false;
            _volumeFadeLock.Release();
        }
    }

    private void ClearActiveBackendSource()
    {
        ActiveBackend.ClearSource();
        _player.Source = null;
    }

    private void OnNativePlaybackFailed(object? sender, PlaybackFailureEventArgs e)
    {
        LogService.Warn("RadioPlayerService", $"Native backend reported failure: {e.Message}");
        Debug.WriteLine($"[RadioPlayerService] Native playback failed: {e.Message}");
        HandleBackendFailure(e);
    }

    private async Task TryFallbackPlaybackAsync()
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return;
        }

        bool wasPlaying = ActiveBackend.IsPlaying;
        LogService.Warn("RadioPlayerService", $"Trying playback fallback (wasPlaying={wasPlaying})");
        PlaybackPrepareResult result = await _playbackEngineSelector.RetryWithFallbackAsync(_streamUrl);
        _lastPrepareError = result.Success ? null : result.ErrorMessage;

        if (!result.Success)
        {
            LogService.Error("RadioPlayerService", $"Fallback prepare failed: {result.ErrorMessage}");
            Debug.WriteLine($"[RadioPlayerService] Fallback prepare failed: {result.ErrorMessage}");
            SetManualBuffering(false);
            ReportPlaybackFailure(result.ErrorMessage, tooManyAttempts: true);
            return;
        }

        SyncActiveBackendVolume();
        if (wasPlaying)
        {
            await PlayActiveBackendWithFadeInAsync(CancellationToken.None);
            StartMetadataForActiveBackend();
            _watchdog.NotifyUserIntentionToPlay();
        }
    }

    private void OnLibVlcPlaybackStateChanged(object? sender, bool isPlaying)
    {
        LogService.Info("RadioPlayerService", $"LibVLC state -> isPlaying={isPlaying}");

        // Reaching Playing means the current attempt succeeded - reset failure tracking.
        if (isPlaying)
        {
            ResetPlaybackFailureTracking();
            ConfirmActiveBackendHealthy();
        }

        TryEnqueueOnUi(() =>
        {
            PlaybackStateChanged?.Invoke(this, isPlaying);
            ScheduleSystemMediaTransportControlsUpdate();
        });
    }

    private void OnLibVlcBackendBufferingStateChanged(object? sender, bool isBuffering)
    {
        TryEnqueueOnUi(() =>
        {
            BufferingStateChanged?.Invoke(this, isBuffering || _isManuallyBuffering);
        });
    }

    private void OnLibVlcPlaybackFailed(object? sender, PlaybackFailureEventArgs e)
    {
        LogService.Warn("RadioPlayerService", $"LibVLC backend reported failure: {e.Message}");
        Debug.WriteLine($"[RadioPlayerService] LibVLC playback failed: {e.Message}");
        HandleBackendFailure(e);
    }

    private void DisposePlaybackEngine()
    {
        _nativeBackend.PlaybackFailed -= OnNativePlaybackFailed;

        if (_libVlcBackend is not null)
        {
            _libVlcBackend.PlaybackStateChanged -= OnLibVlcPlaybackStateChanged;
            _libVlcBackend.BufferingStateChanged -= OnLibVlcBackendBufferingStateChanged;
            _libVlcBackend.PlaybackFailed -= OnLibVlcPlaybackFailed;
        }

        _playbackEngineSelector.Dispose();
        _metadataOrchestrator.Dispose();
        _icyMetadataService.Dispose();
        _hlsSegmentMetadataService.Dispose();
    }
}
