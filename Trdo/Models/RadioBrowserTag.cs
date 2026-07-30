using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// Represents a tag/genre entry from the Radio Browser API (/json/tags)
/// </summary>
public class RadioBrowserTag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stationcount")]
    public int StationCount { get; set; }

    /// <summary>
    /// Display text for dropdowns, e.g. "jazz (321)"
    /// </summary>
    [JsonIgnore]
    public string Display => $"{Name} ({StationCount})";
}
