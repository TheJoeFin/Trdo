using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// Represents a language entry from the Radio Browser API (/json/languages)
/// </summary>
public class RadioBrowserLanguage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("iso_639")]
    public string? Iso639 { get; set; }

    [JsonPropertyName("stationcount")]
    public int StationCount { get; set; }

    /// <summary>
    /// Display text for dropdowns, e.g. "english (5678)"
    /// </summary>
    [JsonIgnore]
    public string Display => $"{Name} ({StationCount})";
}
