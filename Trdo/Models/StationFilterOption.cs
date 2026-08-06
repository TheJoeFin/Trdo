namespace Trdo.Models;

/// <summary>
/// The station attributes that have too many possible values to browse in a dropdown.
/// Codec, bitrate and sort order are deliberately absent: those are short, fixed lists and
/// are better served by an ordinary ComboBox.
/// </summary>
public enum StationFilterFacet
{
    Country,
    Language,
    Genre
}

/// <summary>
/// One value a search can be narrowed by — "Country: Germany", "Genre: jazz" — together with
/// how many stations carry it.
/// <para>
/// The same type serves as a suggestion in the picker and as an applied filter chip, so
/// picking one is just a move between two lists rather than a translation.
/// </para>
/// </summary>
public sealed record StationFilterOption(StationFilterFacet Facet, string Value, int StationCount)
{
    /// <summary>Short badge text identifying which attribute this narrows.</summary>
    public string FacetLabel => Facet switch
    {
        StationFilterFacet.Country => "Country",
        StationFilterFacet.Language => "Language",
        _ => "Genre"
    };

    /// <summary>The station count, formatted for the suggestion row's trailing column.</summary>
    public string CountLabel => StationCount == 1 ? "1 station" : $"{StationCount:N0} stations";

    /// <summary>Chip text: the value alone would be ambiguous once several facets are in play.</summary>
    public string ChipLabel => $"{FacetLabel}: {Value}";
}
