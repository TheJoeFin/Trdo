namespace Trdo.Services.Playback;

/// <summary>
/// User preference for which playback engine to use.
/// Stored values must stay stable across versions.
/// </summary>
public enum PlaybackEngineMode
{
    /// <summary>LibVLC first, native fallback. The default.</summary>
    Auto = 0,

    /// <summary>Windows Media Foundation only, never LibVLC.</summary>
    NativeOnly = 1,

    /// <summary>Legacy pre-2.0 value; reads migrate to <see cref="Auto"/>.</summary>
    LibVlcPreferred = 2,

    /// <summary>Native first, LibVLC fallback (the pre-2.0 Auto behavior).</summary>
    NativePreferred = 3
}
