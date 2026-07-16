using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Windows.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Trdo.Services.Playback;

/// <summary>
/// LibVLC-based playback fallback for streams Windows Media Foundation cannot play.
/// </summary>
public sealed class LibVlcPlaybackBackend : IPlaybackBackend
{
    private readonly LibVLC _libVlc;
    private readonly VlcMediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private bool _isBuffering;

    public LibVlcPlaybackBackend(LibVLC libVlc)
    {
        _libVlc = libVlc;
        _mediaPlayer = new VlcMediaPlayer(_libVlc);

        _mediaPlayer.Playing += (_, _) => RaiseStateChanged();
        _mediaPlayer.Paused += (_, _) => RaiseStateChanged();
        _mediaPlayer.Stopped += (_, _) => RaiseStateChanged();
        _mediaPlayer.EndReached += (_, _) => RaiseStateChanged();
        _mediaPlayer.Buffering += (_, e) =>
        {
            _isBuffering = e.Cache < 100f;
            BufferingStateChanged?.Invoke(this, _isBuffering);
        };
        _mediaPlayer.EncounteredError += (_, _) =>
        {
            Debug.WriteLine("[LibVlcPlaybackBackend] EncounteredError");
            PlaybackFailed?.Invoke(this, new PlaybackFailureEventArgs(
                PlaybackBackendKind.LibVlc,
                "LibVLC playback error",
                canRetryWithFallback: true));
        };
    }

    /// <summary>
    /// Network cache duration passed to LibVLC at prepare time, mirroring the
    /// user's buffer setting. Values of zero or less use the LibVLC default.
    /// </summary>
    public int NetworkCachingMs { get; set; }

    public PlaybackBackendKind Kind => PlaybackBackendKind.LibVlc;

    public Windows.Media.Playback.MediaPlaybackItem? CurrentPlaybackItem => null;

    public VlcMediaPlayer VlcMediaPlayer => _mediaPlayer;

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public bool IsBuffering => _isBuffering;

    public double BufferingProgress => _isBuffering ? 0.5 : 1.0;

    public TimeSpan Position => TimeSpan.FromMilliseconds(_mediaPlayer.Time);

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<bool>? BufferingStateChanged;
    public event EventHandler<PlaybackFailureEventArgs>? PlaybackFailed;

    public IReadOnlyList<MediaTimeRange> GetBufferedRanges() => [];

    public void SetVolume(double volume) => _mediaPlayer.Volume = (int)Math.Clamp(volume * 100, 0, 100);

    public Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        ClearSource();

        _currentMedia = new Media(_libVlc, streamUrl, FromType.FromLocation);
        int networkCachingMs = NetworkCachingMs > 0 ? NetworkCachingMs : 3000;
        _currentMedia.AddOption($":network-caching={networkCachingMs}");
        _mediaPlayer.Media = _currentMedia;

        Debug.WriteLine($"[LibVlcPlaybackBackend] Prepared source for {streamUrl}");
        return Task.FromResult(PlaybackPrepareResult.Succeeded(PlaybackBackendKind.LibVlc));
    }

    public void Play() => _mediaPlayer.Play();

    public void Pause() => _mediaPlayer.Pause();

    public void ClearSource()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
        }

        _mediaPlayer.Media = null;
        _currentMedia?.Dispose();
        _currentMedia = null;
        _isBuffering = false;
    }

    private void RaiseStateChanged()
    {
        PlaybackStateChanged?.Invoke(this, _mediaPlayer.IsPlaying);
        BufferingStateChanged?.Invoke(this, _isBuffering);
    }

    public void Dispose()
    {
        ClearSource();
        _mediaPlayer.Dispose();
    }
}
