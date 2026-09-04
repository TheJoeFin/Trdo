using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Serializes favorited tracks into playlist formats other apps can import.
/// Favorites carry no audio URL, so only formats that allow metadata-only entries are supported:
/// CSV (playlist transfer services such as Soundiiz or TuneMyMusic) and XSPF (VLC, foobar2000).
/// </summary>
public static class FavoriteTrackExportService
{
    public const string CsvExtension = ".csv";
    public const string XspfExtension = ".xspf";

    /// <summary>
    /// Orders tracks the way they should appear in an export: oldest favorite first, so the
    /// playlist reads chronologically.
    /// </summary>
    public static List<FavoriteTrack> OrderForExport(IEnumerable<FavoriteTrack> tracks)
    {
        if (tracks is null)
            return [];

        return [.. tracks.OrderBy(t => t.FavoritedAt)];
    }

    /// <summary>
    /// Builds a suggested file name for an export, without an extension.
    /// </summary>
    public static string BuildSuggestedFileName(DateTime? timestamp = null)
    {
        DateTime stamp = timestamp ?? DateTime.Now;
        return $"Traydio Favorites {stamp:yyyy-MM-dd}";
    }

    /// <summary>
    /// Serializes tracks to the format matching the given file extension.
    /// Falls back to CSV for unrecognized extensions.
    /// </summary>
    public static string Export(IEnumerable<FavoriteTrack> tracks, string extension)
    {
        return string.Equals(extension?.Trim(), XspfExtension, StringComparison.OrdinalIgnoreCase)
            ? ExportToXspf(tracks)
            : ExportToCsv(tracks);
    }

    /// <summary>
    /// Exports tracks to RFC 4180 CSV with a header row understood by playlist transfer services.
    /// The Album column is always empty because radio metadata does not include album information,
    /// but it is present because most importers expect it.
    /// </summary>
    public static string ExportToCsv(IEnumerable<FavoriteTrack> tracks)
    {
        StringBuilder sb = new();
        sb.Append("Title,Artist,Album,Station,Favorited\r\n");

        foreach (FavoriteTrack track in OrderForExport(tracks))
        {
            sb.Append(EscapeCsv(GetTitle(track)));
            sb.Append(',');
            sb.Append(EscapeCsv(track.Artist ?? string.Empty));
            sb.Append(',');
            sb.Append(EscapeCsv(string.Empty));
            sb.Append(',');
            sb.Append(EscapeCsv(track.StationName ?? string.Empty));
            sb.Append(',');
            sb.Append(EscapeCsv(track.FavoritedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports tracks to XSPF. Tracks are emitted without a location element, which the spec
    /// allows and which players render as metadata-only entries.
    /// </summary>
    public static string ExportToXspf(IEnumerable<FavoriteTrack> tracks, DateTime? generatedAt = null)
    {
        DateTime stamp = generatedAt ?? DateTime.Now;

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
        sb.Append("<playlist version=\"1\" xmlns=\"http://xspf.org/ns/0/\">\r\n");
        sb.Append("  <title>Traydio Favorites</title>\r\n");
        sb.Append("  <creator>Traydio</creator>\r\n");
        sb.Append($"  <date>{stamp.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}</date>\r\n");
        sb.Append("  <trackList>\r\n");

        foreach (FavoriteTrack track in OrderForExport(tracks))
        {
            sb.Append("    <track>\r\n");
            sb.Append($"      <title>{EscapeXml(GetTitle(track))}</title>\r\n");

            if (!string.IsNullOrWhiteSpace(track.Artist))
                sb.Append($"      <creator>{EscapeXml(track.Artist)}</creator>\r\n");

            if (!string.IsNullOrWhiteSpace(track.StationName))
                sb.Append($"      <annotation>{EscapeXml(track.StationName)}</annotation>\r\n");

            sb.Append("    </track>\r\n");
        }

        sb.Append("  </trackList>\r\n");
        sb.Append("</playlist>\r\n");

        return sb.ToString();
    }

    private static string GetTitle(FavoriteTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Title))
            return track.Title;

        return !string.IsNullOrWhiteSpace(track.StreamTitle)
            ? track.StreamTitle
            : track.DisplayText;
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;

        bool needsQuotes = value.Contains(',') || value.Contains('"')
            || value.Contains('\r') || value.Contains('\n');

        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string EscapeXml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
