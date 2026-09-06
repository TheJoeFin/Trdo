namespace Trdo.Models;

/// <summary>
/// The noise spectrum generated for a white noise "station". See
/// <see cref="RadioStation.SourceKind"/> is <see cref="AudioSourceKind.WhiteNoise"/>.
/// </summary>
public enum WhiteNoiseColor
{
    /// <summary>Flat energy across the whole audible band - a steady, even hiss.</summary>
    White,

    /// <summary>Energy falls off with frequency, reading as softer and less hissy than white.</summary>
    Pink
}
