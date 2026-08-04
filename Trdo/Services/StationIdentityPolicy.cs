using System;
using System.Collections.Generic;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Assigns stable ids to stations that do not have one.
/// <para>
/// Stations saved before 2.0 have no <see cref="RadioStation.Id"/>, and a station written
/// by an older build loses the field entirely (older builds do not know about it, so it is
/// dropped on their next save). Both cases show up here as an empty id, and both are
/// handled the same way: stamp a fresh one and persist.
/// </para>
/// </summary>
public static class StationIdentityPolicy
{
    /// <summary>
    /// Stamps a fresh id onto every station that has none, leaving existing ids untouched.
    /// </summary>
    /// <returns>
    /// True if any station was changed, so the caller knows whether it needs to write the
    /// list back out. A load that changes nothing must not trigger a save.
    /// </returns>
    public static bool EnsureIds(IEnumerable<RadioStation>? stations)
    {
        if (stations is null)
            return false;

        bool changed = false;
        foreach (RadioStation station in stations)
        {
            if (station is null || !string.IsNullOrWhiteSpace(station.Id))
                continue;

            station.Id = NewId();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Creates a new station id: 32 hex characters, with no braces or dashes so it needs no
    /// escaping wherever it is used as a key.
    /// </summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
