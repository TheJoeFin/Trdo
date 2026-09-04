using System;
using System.Collections.Generic;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Resolves which station should be selected on startup from what was persisted.
/// <para>
/// Selection used to be stored as a bare index into the station list. That stopped being
/// workable once folders, collapsing and view sorts could move a station: the index would
/// silently point at a different station. Selection is now stored by id, with the old index
/// still written alongside it so that rolling back to an older build restores the right
/// station instead of falling back to the first one.
/// </para>
/// </summary>
public static class StationSelectionPolicy
{
    /// <summary>
    /// Picks the station to restore, preferring the saved id and falling back through the
    /// legacy index to the first station.
    /// </summary>
    /// <param name="stations">The loaded stations, in file order.</param>
    /// <param name="savedId">The persisted station id, or null/empty on first run after upgrading.</param>
    /// <param name="legacyIndex">The persisted index, still written by this build and by pre-2.0 builds.</param>
    /// <returns>The station to select, or null when there are no stations.</returns>
    public static RadioStation? Resolve(IReadOnlyList<RadioStation>? stations, string? savedId, int legacyIndex)
    {
        if (stations is null || stations.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(savedId))
        {
            foreach (RadioStation station in stations)
            {
                if (string.Equals(station.Id, savedId, StringComparison.Ordinal))
                    return station;
            }
        }

        // No id, or an id that no longer exists (the station was removed). The index is
        // stale in exactly the cases the id was introduced to fix, but it is still a better
        // guess than always starting at the top.
        if (legacyIndex >= 0 && legacyIndex < stations.Count)
            return stations[legacyIndex];

        return stations[0];
    }
}
