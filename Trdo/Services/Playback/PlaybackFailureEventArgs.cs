using System;

namespace Trdo.Services.Playback;

public sealed class PlaybackFailureEventArgs : EventArgs
{
    public PlaybackFailureEventArgs(PlaybackBackendKind backend, string message, bool canRetryWithFallback)
    {
        Backend = backend;
        Message = message;
        CanRetryWithFallback = canRetryWithFallback;
    }

    public PlaybackBackendKind Backend { get; }
    public string Message { get; }
    public bool CanRetryWithFallback { get; }
}
