using System.Collections.Generic;

namespace Trdo.Models;

/// <summary>
/// The persisted shape of the station list's structure: which stations sit in which folder,
/// where the dividers are, and which folders are collapsed.
/// <para>
/// Deliberately a <em>sidecar</em> file rather than part of <c>stations.json</c>. The station
/// file has to stay a bare array that a pre-2.0 build can parse, because such a build treats
/// a file it cannot read as empty and then overwrites it on quit. Keeping structure in its
/// own file means an older build simply ignores it: rolling back loses the folders, not the
/// stations.
/// </para>
/// </summary>
public sealed class StationLayoutDocument
{
    /// <summary>
    /// Format version, for the benefit of a future reader that needs to tell shapes apart.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>The top-level rows, in display order.</summary>
    public List<StationLayoutRow> Rows { get; set; } = [];
}

/// <summary>
/// One persisted row.
/// <para>
/// Deliberately a single "wide" type covering all three row kinds rather than a polymorphic
/// hierarchy, so the file needs no type discriminator machinery. System.Text.Json's
/// polymorphism does work with a source-generated context, but a mistake in it surfaces only
/// at runtime in a trimmed Release build - and the three shapes are small enough that a union
/// type costs less than that risk. Unrecognised <see cref="Kind"/> values are simply dropped
/// when the layout is reconciled, which makes the format forward-compatible for free.
/// </para>
/// </summary>
public sealed class StationLayoutRow
{
    /// <summary>One of <c>station</c>, <c>group</c> or <c>divider</c>.</summary>
    public string Kind { get; set; } = StationLayoutKinds.Station;

    /// <summary>For a station row: the id of the station this row refers to.</summary>
    public string? StationId { get; set; }

    /// <summary>
    /// For a station row: the station's stream URL, stored purely as a recovery key.
    /// <para>
    /// If the user runs an older build, it rewrites <c>stations.json</c> without the id field,
    /// and every station comes back with a freshly stamped id that matches nothing in here.
    /// The stream URL survives that round trip, so it is what lets the folders be put back
    /// together afterwards instead of collapsing into a flat list.
    /// </para>
    /// </summary>
    public string? StationUrl { get; set; }

    /// <summary>For a group or divider row: that item's own id.</summary>
    public string? Id { get; set; }

    /// <summary>For a group row: the folder's name.</summary>
    public string? Name { get; set; }

    /// <summary>For a divider row: the optional caption.</summary>
    public string? Label { get; set; }

    /// <summary>For a group row: whether the folder is expanded.</summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>For a group row: the folder's contents, in order.</summary>
    public List<StationLayoutRow>? Children { get; set; }
}

/// <summary>The recognised values of <see cref="StationLayoutRow.Kind"/>.</summary>
public static class StationLayoutKinds
{
    public const string Station = "station";
    public const string Group = "group";
    public const string Divider = "divider";
}
