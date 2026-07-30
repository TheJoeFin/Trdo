using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;

namespace Trdo.Services.Playback;

/// <summary>
/// Windows MediaPlayer-based playback with MediaPlaybackItem and AdaptiveMediaSource for HLS.
/// </summary>
public sealed class NativePlaybackBackend : IPlaybackBackend
{
    private readonly MediaPlayer _player;
    private readonly HttpClient _httpClient;
    private MediaPlaybackItem? _currentPlaybackItem;

    public NativePlaybackBackend(MediaPlayer player, HttpClient httpClient)
    {
        _player = player;
        _httpClient = httpClient;

        _player.MediaFailed += OnMediaFailed;
    }

    public PlaybackBackendKind Kind => PlaybackBackendKind.Native;

    public MediaPlaybackItem? CurrentPlaybackItem => _currentPlaybackItem;

    public bool IsPlaying =>
        _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public bool IsBuffering
    {
        get
        {
            try
            {
                MediaPlaybackState state = _player.PlaybackSession.PlaybackState;
                return state is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
            }
            catch
            {
                return false;
            }
        }
    }

    public double BufferingProgress
    {
        get
        {
            try
            {
                return _player.PlaybackSession.BufferingProgress;
            }
            catch
            {
                return 0;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            try
            {
                return _player.PlaybackSession.Position;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<bool>? BufferingStateChanged;
    public event EventHandler<PlaybackFailureEventArgs>? PlaybackFailed;

    public IReadOnlyList<MediaTimeRange> GetBufferedRanges()
    {
        try
        {
            return _player.PlaybackSession.GetBufferedRanges();
        }
        catch
        {
            return [];
        }
    }

    // Windows MediaPlayer only supports 0.0-1.0, so amplification above 100% is
    // capped here; the LibVLC backend handles true >100% amplification.
    public void SetVolume(double volume) => _player.Volume = Math.Clamp(volume, 0, 1);

    public async Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        ClearSource();

        (MediaPlaybackItem? item, string? error) =
            await HlsStreamHelper.CreatePlaybackItemAsync(streamUrl, _httpClient, cancellationToken);

        if (item is null)
        {
            return PlaybackPrepareResult.Failed(PlaybackBackendKind.Native, error ?? "Failed to create playback item");
        }

        _currentPlaybackItem = item;
        _player.Source = item;
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.RealTimePlayback = true;

        Debug.WriteLine($"[NativePlaybackBackend] Prepared source for {streamUrl}");
        return PlaybackPrepareResult.Succeeded(PlaybackBackendKind.Native);
    }

    public void Play()
    {
        _player.Play();
    }

    public void Pause()
    {
        _player.Pause();
    }

    public void ClearSource()
    {
        MediaPlaybackItemHelper.DisposePlayerSource(_player.Source);
        _player.Source = null;
        _currentPlaybackItem = null;
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        string message = args.ErrorMessage ?? args.Error.ToString();
        Debug.WriteLine($"[NativePlaybackBackend] MediaFailed: {message}");
        PlaybackFailed?.Invoke(this, new PlaybackFailureEventArgs(PlaybackBackendKind.Native, message, canRetryWithFallback: true));
    }

    public void Dispose()
    {
        _player.MediaFailed -= OnMediaFailed;
        ClearSource();
    }
}
