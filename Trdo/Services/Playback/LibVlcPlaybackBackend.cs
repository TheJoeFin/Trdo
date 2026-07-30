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
    private VlcMediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private bool _isBuffering;

    public LibVlcPlaybackBackend(LibVLC libVlc)
    {
        _libVlc = libVlc;
        _mediaPlayer = CreateMediaPlayer();
    }

    private VlcMediaPlayer CreateMediaPlayer()
    {
        VlcMediaPlayer player = new(_libVlc);

        player.Playing += OnPlayerStateChanged;
        player.Paused += OnPlayerStateChanged;
        player.Stopped += OnPlayerStateChanged;
        player.EndReached += OnPlayerStateChanged;
        player.Buffering += OnPlayerBuffering;
        player.EncounteredError += OnPlayerEncounteredError;

        return player;
    }

    private void DetachMediaPlayer(VlcMediaPlayer player)
    {
        player.Playing -= OnPlayerStateChanged;
        player.Paused -= OnPlayerStateChanged;
        player.Stopped -= OnPlayerStateChanged;
        player.EndReached -= OnPlayerStateChanged;
        player.Buffering -= OnPlayerBuffering;
        player.EncounteredError -= OnPlayerEncounteredError;
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e) => RaiseStateChanged();

    private void OnPlayerBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        _isBuffering = e.Cache < 100f;
        BufferingStateChanged?.Invoke(this, _isBuffering);
    }

    private void OnPlayerEncounteredError(object? sender, EventArgs e)
    {
        Debug.WriteLine("[LibVlcPlaybackBackend] EncounteredError");
        PlaybackFailed?.Invoke(this, new PlaybackFailureEventArgs(
            PlaybackBackendKind.LibVlc,
            "LibVLC playback error",
            canRetryWithFallback: true));
    }

    /// <summary>
    /// Disposes the current media player and creates a fresh one on the same LibVLC
    /// instance. Used by the recovery ladder when re-preparing the media alone hasn't
    /// restored playback and the player's internal pipeline is suspect.
    /// </summary>
    public void Recycle()
    {
        Debug.WriteLine("[LibVlcPlaybackBackend] Recycling media player");

        VlcMediaPlayer old = _mediaPlayer;
        ClearSource();
        DetachMediaPlayer(old);

        _mediaPlayer = CreateMediaPlayer();

        try
        {
            old.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LibVlcPlaybackBackend] Error disposing recycled player: {ex.Message}");
        }
    }

    /// <summary>
    /// Network cache duration passed to LibVLC at prepare time, mirroring the
    /// user's buffer setting. Values of zero or less use the LibVLC default.
    /// </summary>
    public int NetworkCachingMs { get; set; }

    /// <summary>LibVLC's own default when <see cref="NetworkCachingMs"/> is unset.</summary>
    public const int DefaultNetworkCachingMs = 3000;

    /// <summary>
    /// The network cache value that would actually be handed to LibVLC, after the default
    /// substitution. Logging <see cref="NetworkCachingMs"/> directly is misleading, because
    /// a value of zero silently becomes <see cref="DefaultNetworkCachingMs"/>.
    /// </summary>
    public int EffectiveNetworkCachingMs =>
        NetworkCachingMs > 0 ? NetworkCachingMs : DefaultNetworkCachingMs;

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

    // volume is a fraction where 1.0 == 100%; LibVLC accepts 0-200 (200% = amplified).
    public void SetVolume(double volume) => _mediaPlayer.Volume = (int)Math.Clamp(volume * 100, 0, 200);

    public Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        ClearSource();

        _currentMedia = new Media(_libVlc, streamUrl, FromType.FromLocation);
        _currentMedia.AddOption($":network-caching={EffectiveNetworkCachingMs}");
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
