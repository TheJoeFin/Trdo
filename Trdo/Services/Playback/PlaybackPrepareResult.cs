namespace Trdo.Services.Playback;

public sealed class PlaybackPrepareResult
{
    public bool Success { get; init; }
    public PlaybackBackendKind Backend { get; init; }
    public string? ErrorMessage { get; init; }
    public bool UsedFallback { get; init; }

    public static PlaybackPrepareResult Succeeded(PlaybackBackendKind backend, bool usedFallback = false) =>
        new() { Success = true, Backend = backend, UsedFallback = usedFallback };

    public static PlaybackPrepareResult Failed(PlaybackBackendKind backend, string errorMessage) =>
        new() { Success = false, Backend = backend, ErrorMessage = errorMessage };
}
