using System;

namespace Trdo.Services;

/// <summary>
/// Pure decision logic for whether a stream metadata change should trigger the
/// song-change popup. Kept free of WinRT/WinUI dependencies so it can be unit
/// tested directly (see Trdo.Tests).
/// </summary>
public static class SongChangeAnnouncementPolicy
{
    /// <summary>
    /// Decides whether the current metadata change is "meaningful" enough to
    /// announce, given the caller-tracked baseline from the previous call.
    /// </summary>
    /// <param name="previousDisplayText">
    /// The meaningful display text observed on the previous call, or
    /// <see langword="null"/> if this is the first observation since launch
    /// (or since the tracker was reset). A null or blank baseline never
    /// announces: it only establishes the starting point so that already-current
    /// metadata does not immediately pop up when the feature is enabled or the
    /// app starts.
    /// </param>
    /// <param name="currentDisplayText">The current <c>StreamMetadata.DisplayText</c>.</param>
    /// <param name="isEnabled">Whether the user has opted into the popup.</param>
    /// <returns><see langword="true"/> if the popup should be shown for this change.</returns>
    public static bool ShouldAnnounce(string? previousDisplayText, string currentDisplayText, bool isEnabled)
    {
        // First observation establishes the baseline only — never announces.
        // This keeps startup and "just enabled the setting" quiet for whatever
        // is already playing.
        if (string.IsNullOrWhiteSpace(previousDisplayText))
            return false;

        if (!isEnabled)
            return false;

        if (string.IsNullOrWhiteSpace(currentDisplayText))
            return false;

        return !string.Equals(
            currentDisplayText.Trim(),
            previousDisplayText.Trim(),
            StringComparison.Ordinal);
    }

    /// <summary>No delay — the popup appears as soon as the metadata changes.</summary>
    public const double MinDelaySeconds = 0;

    /// <summary>
    /// Upper bound on the announcement delay. A minute covers even the worst offenders,
    /// and stopping there keeps an unbounded value from letting a popup outlive the song
    /// it describes.
    /// </summary>
    public const double MaxDelaySeconds = 60;

    /// <summary>Constrains a delay to the supported range, mapping NaN to no delay.</summary>
    public static double ClampDelay(double seconds)
    {
        if (double.IsNaN(seconds))
            return MinDelaySeconds;

        return Math.Clamp(seconds, MinDelaySeconds, MaxDelaySeconds);
    }

    /// <summary>
    /// Works out how long to wait before announcing, given the station's own override and
    /// the app-wide setting.
    /// <para>
    /// Many stations push metadata a few seconds before the audio actually reaches the
    /// listener, and the size of that lead is a property of the station's encoder — so the
    /// per-station value wins outright rather than adding to the global one. A station with
    /// no override follows the app setting.
    /// </para>
    /// </summary>
    /// <param name="stationDelaySeconds">The station's override, or null to follow the app setting.</param>
    /// <param name="globalDelaySeconds">The app-wide delay.</param>
    public static double ResolveDelaySeconds(double? stationDelaySeconds, double globalDelaySeconds)
    {
        return ClampDelay(stationDelaySeconds ?? globalDelaySeconds);
    }

    /// <summary>
    /// Formats a delay for display, so the settings page, the station editor and the
    /// popup's own menu all describe the same value identically.
    /// </summary>
    public static string DescribeDelay(double seconds)
    {
        double clamped = ClampDelay(seconds);

        if (clamped <= 0)
            return "No delay";

        // The top of the range reads better as a minute than as 60 seconds.
        if (clamped >= MaxDelaySeconds)
            return "1 minute";

        // Whole seconds read better than "5.0s" for the common preset values.
        return Math.Abs(clamped - Math.Round(clamped)) < 0.05
            ? $"{Math.Round(clamped):0} second{(Math.Round(clamped) == 1 ? "" : "s")}"
            : $"{clamped:0.0} seconds";
    }
}