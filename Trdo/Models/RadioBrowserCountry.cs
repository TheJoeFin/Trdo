using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// Represents a country entry from the Radio Browser API (/json/countries)
/// </summary>
public class RadioBrowserCountry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("iso_3166_1")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("stationcount")]
    public int StationCount { get; set; }

    /// <summary>
    /// Display text for dropdowns, e.g. "Germany (1234)"
    /// </summary>
    [JsonIgnore]
    public string Display => $"{Name} ({StationCount})";
}
