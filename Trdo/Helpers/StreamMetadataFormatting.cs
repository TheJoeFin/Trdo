using Trdo.Models;

namespace Trdo.Helpers;

public static class StreamMetadataFormatting
{
    public const string NotAvailable = "N/A";

    public static string FormatArtist(StreamMetadata? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.Artist))
        {
            return metadata.Artist.Trim();
        }

        return NotAvailable;
    }

    public static string FormatTitle(StreamMetadata? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.Title))
        {
            return metadata.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(metadata?.StreamTitle))
        {
            return metadata.StreamTitle.Trim();
        }

        return NotAvailable;
    }
}
