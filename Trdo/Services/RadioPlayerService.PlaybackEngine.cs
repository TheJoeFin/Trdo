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

        PlaybackPrepareResult result = await _playbackEngineSelector.PrepareAsync(_streamUrl, cancellationToken);
        _lastPrepareError = result.Success ? null : result.ErrorMessage;

        if (!result.Success)
        {
            return result;
        }

        if (result.Backend == PlaybackBackendKind.Native &&
            _libVlcBackend is not null &&
            PlaybackEngineSelector.GetEngineMode() == PlaybackEngineMode.Auto)
        {
            bool opened = await _playbackEngineSelector.WaitForNativeOpenAsync(
                cancellationToken,
                TimeSpan.FromSeconds(15));

            if (!opened)
            {
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
        if (string.IsNullOrWhiteSpace(_streamUrl) || _libVlcBackend is null)
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

    private void SyncActiveBackendVolume()
    {
        ActiveBackend.SetVolume(_volume);
    }

    private void ClearActiveBackendSource()
    {
        ActiveBackend.ClearSource();
        _player.Source = null;
    }

    private void OnNativePlaybackFailed(object? sender, PlaybackFailureEventArgs e)
    {
        if (!e.CanRetryWithFallback || _libVlcBackend is null)
        {
            return;
        }

        _ = TryFallbackPlaybackAsync();
    }

    private async Task TryFallbackPlaybackAsync()
    {
        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            return;
        }

        bool wasPlaying = ActiveBackend.IsPlaying;
        PlaybackPrepareResult result = await _playbackEngineSelector.RetryWithFallbackAsync(_streamUrl);
        _lastPrepareError = result.Success ? null : result.ErrorMessage;

        if (!result.Success)
        {
            return;
        }

        SyncActiveBackendVolume();
        if (wasPlaying)
        {
            ActiveBackend.Play();
            StartMetadataForActiveBackend();
            _watchdog.NotifyUserIntentionToPlay();
        }
    }

    private void OnLibVlcPlaybackStateChanged(object? sender, bool isPlaying)
    {
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
        Debug.WriteLine($"[RadioPlayerService] LibVLC playback failed: {e.Message}");
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
