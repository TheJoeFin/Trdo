namespace Trdo.Models;

/// <summary>
/// Represents metadata extracted from an internet radio stream, typically from ICY (Icecast/Shoutcast) protocol.
/// </summary>
public class StreamMetadata
{
    /// <summary>
    /// The full stream title string, typically containing song and artist info.
    /// Format is usually "Artist - Title" or similar.
    /// </summary>
    public string StreamTitle { get; set; } = string.Empty;

    /// <summary>
    /// The artist name, if available.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// The song/track title, if available.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The URL to album artwork, if available.
    /// </summary>
    public string? AlbumArtUrl { get; set; }

    /// <summary>
    /// Indicates whether any meaningful metadata was found.
    /// </summary>
    public bool HasMetadata => !string.IsNullOrWhiteSpace(StreamTitle) ||
                               !string.IsNullOrWhiteSpace(Artist) ||
                               !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Gets a display-friendly string for the now playing information.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title))
                return $"{Artist} - {Title}";
            
            if (!string.IsNullOrWhiteSpace(StreamTitle))
                return StreamTitle;
            
            return string.Empty;
        }
    }

    /// <summary>
    /// Creates a new StreamMetadata with no data.
    /// </summary>
    public static StreamMetadata Empty => new();
}
