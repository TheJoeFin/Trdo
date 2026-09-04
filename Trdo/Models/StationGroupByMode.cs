namespace Trdo.Models;

/// <summary>
/// How the station list is grouped into folders on screen.
/// <para>
/// The folder-shaped counterpart to <see cref="StationSortMode"/>: every value except
/// <see cref="None"/> is a <em>view</em> arrangement. It buckets stations into synthetic
/// folders keyed by a field and leaves the user's own folders, dividers and manual order
/// exactly as they are, so switching back to <see cref="None"/> restores them untouched.
/// </para>
/// <para>
/// Mutually exclusive with a view sort - a flat sorted list and stations bucketed into
/// folders cannot both be true of the list at once, so picking one turns the other off.
/// </para>
/// </summary>
public enum StationGroupByMode
{
    /// <summary>The user's own folders, from the saved arrangement. The default.</summary>
    None = 0,

    /// <summary>One synthetic folder per genre.</summary>
    Genre = 1,

    /// <summary>One synthetic folder per country.</summary>
    Country = 2
}
