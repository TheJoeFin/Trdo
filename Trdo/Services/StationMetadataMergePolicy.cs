using System;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Copies directory details onto a saved station.
/// <para>
/// Five fields are never touched, whatever the directory says: <c>Name</c>, <c>StreamUrl</c>,
/// <c>Volume</c>, <c>BufferLevel</c> and <c>SongPopupDelaySeconds</c>. Those are the user's own
/// decisions - a renamed station, a level they tuned by ear, a buffer they raised because that
/// stream stutters - and a lookup has no business overruling them. This applies even when
/// overwriting is asked for; "refresh the details" means the genre and the country, not the
/// settings.
/// </para>
/// </summary>
public static class StationMetadataMergePolicy
{
    /// <summary>
    /// Fills in a station's directory details.
    /// </summary>
    /// <param name="local">The saved station to update.</param>
    /// <param name="remote">The matching directory entry.</param>
    /// <param name="overwriteExisting">
    /// When false, only fields the station has no value for are filled. When true, details it
    /// already has are replaced - still excluding the five listed on this class.
    /// </param>
    /// <returns>
    /// True if anything actually changed, so a refresh that found nothing new does not trigger
    /// a write.
    /// </returns>
    public static bool Merge(RadioStation local, RadioBrowserStation remote, bool overwriteExisting)
    {
        if (local is null || remote is null)
            return false;

        bool changed = false;

        changed |= SetText(local.StationUuid, remote.StationUuid, overwriteExisting, v => local.StationUuid = v);
        changed |= SetText(local.Tags, remote.Tags, overwriteExisting, v => local.Tags = v);
        changed |= SetText(local.Country, remote.Country, overwriteExisting, v => local.Country = v);
        changed |= SetText(local.CountryCode, remote.CountryCode, overwriteExisting, v => local.CountryCode = v);
        changed |= SetText(local.Language, remote.Language, overwriteExisting, v => local.Language = v);
        changed |= SetText(local.Codec, remote.Codec, overwriteExisting, v => local.Codec = v);

        // Homepage and favicon are shown in the UI and may well have been left blank when the
        // station was typed in by hand, so filling a gap is welcome - but replacing a chosen
        // one is not, hence never overwritten.
        changed |= SetText(local.Homepage, remote.Homepage, overwriteExisting: false, v => local.Homepage = v);
        changed |= SetText(local.FaviconUrl, remote.Favicon, overwriteExisting: false, v => local.FaviconUrl = v);

        if (remote.Bitrate > 0 && (overwriteExisting || local.Bitrate is null) && local.Bitrate != remote.Bitrate)
        {
            local.Bitrate = remote.Bitrate;
            changed = true;
        }

        // Always stamped, even when nothing else moved, so a station the directory genuinely
        // has nothing new for is not retried on every run.
        local.MetadataRefreshedUtc = DateTimeOffset.UtcNow;

        // DateAdded is deliberately left alone: it records when this user added the station,
        // which the directory knows nothing about.

        return changed;
    }

    private static bool SetText(string? current, string? incoming, bool overwriteExisting, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return false;

        if (!overwriteExisting && !string.IsNullOrWhiteSpace(current))
            return false;

        string trimmed = incoming.Trim();
        if (string.Equals(current, trimmed, StringComparison.Ordinal))
            return false;

        apply(trimmed);
        return true;
    }
}
