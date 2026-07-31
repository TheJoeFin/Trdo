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
}