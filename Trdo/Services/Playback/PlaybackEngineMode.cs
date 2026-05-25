namespace Trdo.Services.Playback;

/// <summary>
/// User preference for which playback engine to use.
/// </summary>
public enum PlaybackEngineMode
{
    Auto = 0,
    NativeOnly = 1,
    LibVlcPreferred = 2
}
