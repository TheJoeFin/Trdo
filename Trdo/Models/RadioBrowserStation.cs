using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// Represents a radio station from the Radio Browser API
/// </summary>
public class RadioBrowserStation
{
    [JsonPropertyName("changeuuid")]
    public string ChangeUuid { get; set; } = string.Empty;

    [JsonPropertyName("stationuuid")]
    public string StationUuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("url_resolved")]
    public string UrlResolved { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("favicon")]
    public string Favicon { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("countrycode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    /// <summary>
    /// Gets the best available stream URL (resolved URL if available, otherwise regular URL)
    /// </summary>
    public string GetStreamUrl()
    {
        return !string.IsNullOrWhiteSpace(UrlResolved) ? UrlResolved : Url;
    }

    /// <summary>
    /// Converts this RadioBrowserStation to a RadioStation for local storage
    /// </summary>
    public RadioStation ToRadioStation()
    {
        return new RadioStation
        {
            Name = Name,
            StreamUrl = GetStreamUrl(),
            Homepage = !string.IsNullOrWhiteSpace(Homepage) ? Homepage : null,
            FaviconUrl = !string.IsNullOrWhiteSpace(Favicon) ? Favicon : null
        };
    }
}
