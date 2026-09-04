namespace Trdo.Models;

/// <summary>
/// How the station list is ordered on screen.
/// <para>
/// Everything except <see cref="Manual"/> is a <em>view</em> sort: it changes what is drawn
/// and nothing else. The hand-built order, the folders and the dividers are all left exactly
/// as they were, and switching back to <see cref="Manual"/> restores them.
/// </para>
/// <para>
/// Values are explicit because this is persisted as an integer, following the same pattern as
/// <see cref="Services.PlaybackEngineMode"/>.
/// </para>
/// </summary>
public enum StationSortMode
{
    /// <summary>The user's own order, with folders and dividers. The default.</summary>
    Manual = 0,

    /// <summary>Alphabetical by station name.</summary>
    Name = 1,

    /// <summary>By the station's first tag.</summary>
    Genre = 2,

    /// <summary>By the station's country.</summary>
    Country = 3,

    /// <summary>Most recently added first.</summary>
    RecentlyAdded = 4
}
