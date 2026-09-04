using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;

namespace Trdo.Services.Playback;

public interface IPlaybackBackend : IDisposable
{
    PlaybackBackendKind Kind { get; }

    bool IsPlaying { get; }
    bool IsBuffering { get; }
    double BufferingProgress { get; }
    TimeSpan Position { get; }

    event EventHandler<bool>? PlaybackStateChanged;
    event EventHandler<bool>? BufferingStateChanged;
    event EventHandler<PlaybackFailureEventArgs>? PlaybackFailed;

    IReadOnlyList<MediaTimeRange> GetBufferedRanges();
    void SetVolume(double volume);
    Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void ClearSource();
    MediaPlaybackItem? CurrentPlaybackItem { get; }
}
