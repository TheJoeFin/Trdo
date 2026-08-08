using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers the favorites export writers. Favorites hold free-text radio metadata, so the two
/// things that matter are that separators inside artist and title text cannot break the file,
/// and that the archive filter only ever hands unexported tracks to the writer.
/// </summary>
[TestClass]
public sealed class FavoriteTrackExportTests
{
    private static FavoriteTrack Track(
        string artist,
        string title,
        string station = "Test FM",
        DateTime? favoritedAt = null,
        DateTime? exportedAt = null) =>
        new()
        {
            Artist = artist,
            Title = title,
            StreamTitle = $"{artist} - {title}",
            StationName = station,
            FavoritedAt = favoritedAt ?? new DateTime(2026, 1, 2, 3, 4, 5),
            ExportedAt = exportedAt,
        };

    private static string[] Lines(string content) =>
        content.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries);

    [TestMethod]
    public void ExportToCsv_WritesHeaderAndRow()
    {
        string csv = FavoriteTrackExportService.ExportToCsv([Track("Nils Frahm", "Says")]);
        string[] lines = Lines(csv);

        Assert.AreEqual("Title,Artist,Album,Station,Favorited", lines[0]);
        Assert.AreEqual("Says,Nils Frahm,,Test FM,2026-01-02 03:04:05", lines[1]);
        Assert.AreEqual(2, lines.Length);
    }

    [TestMethod]
    public void ExportToCsv_QuotesFieldsContainingSeparators()
    {
        FavoriteTrack track = Track("Earth, Wind & Fire", "Say \"Yes\"", "Line\nBreak FM");
        string[] lines = Lines(FavoriteTrackExportService.ExportToCsv([track]));

        Assert.IsTrue(lines[1].StartsWith("\"Say \"\"Yes\"\"\",\"Earth, Wind & Fire\",,\"Line", StringComparison.Ordinal));
        Assert.IsTrue(FavoriteTrackExportService.ExportToCsv([track]).Contains("\"Line\nBreak FM\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExportToCsv_FallsBackToStreamTitleWhenTitleMissing()
    {
        FavoriteTrack track = new()
        {
            StreamTitle = "Unparsed stream text",
            StationName = "Test FM",
            FavoritedAt = new DateTime(2026, 1, 2, 3, 4, 5),
        };

        Assert.AreEqual(
            "Unparsed stream text,,,Test FM,2026-01-02 03:04:05",
            Lines(FavoriteTrackExportService.ExportToCsv([track]))[1]);
    }

    [TestMethod]
    public void ExportToCsv_EmptyCollectionWritesHeaderOnly()
    {
        Assert.AreEqual(1, Lines(FavoriteTrackExportService.ExportToCsv([])).Length);
    }

    [TestMethod]
    public void OrderForExport_SortsOldestFavoriteFirst()
    {
        List<FavoriteTrack> ordered = FavoriteTrackExportService.OrderForExport(
        [
            Track("B", "Newer", favoritedAt: new DateTime(2026, 3, 1)),
            Track("A", "Older", favoritedAt: new DateTime(2026, 1, 1)),
        ]);

        Assert.AreEqual("Older", ordered[0].Title);
        Assert.AreEqual("Newer", ordered[1].Title);
    }

    [TestMethod]
    public void ExportToXspf_ProducesWellFormedMetadataOnlyTracks()
    {
        string xml = FavoriteTrackExportService.ExportToXspf([Track("Nils Frahm", "Says")]);
        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = "http://xspf.org/ns/0/";

        XElement track = doc.Root!.Element(ns + "trackList")!.Elements(ns + "track").Single();
        Assert.AreEqual("Says", track.Element(ns + "title")!.Value);
        Assert.AreEqual("Nils Frahm", track.Element(ns + "creator")!.Value);
        Assert.AreEqual("Test FM", track.Element(ns + "annotation")!.Value);
        Assert.IsNull(track.Element(ns + "location"));
    }

    [TestMethod]
    public void ExportToXspf_EscapesMarkupCharacters()
    {
        string xml = FavoriteTrackExportService.ExportToXspf([Track("Simon & Garfunkel", "<Sound> of Silence")]);
        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = "http://xspf.org/ns/0/";

        XElement track = doc.Root!.Element(ns + "trackList")!.Elements(ns + "track").Single();
        Assert.AreEqual("<Sound> of Silence", track.Element(ns + "title")!.Value);
        Assert.AreEqual("Simon & Garfunkel", track.Element(ns + "creator")!.Value);
    }

    [TestMethod]
    public void ExportToXspf_OmitsEmptyOptionalElements()
    {
        FavoriteTrack track = new()
        {
            StreamTitle = "Unparsed stream text",
            FavoritedAt = new DateTime(2026, 1, 2),
        };

        XDocument doc = XDocument.Parse(FavoriteTrackExportService.ExportToXspf([track]));
        XNamespace ns = "http://xspf.org/ns/0/";

        XElement exported = doc.Root!.Element(ns + "trackList")!.Elements(ns + "track").Single();
        Assert.AreEqual("Unparsed stream text", exported.Element(ns + "title")!.Value);
        Assert.IsNull(exported.Element(ns + "creator"));
        Assert.IsNull(exported.Element(ns + "annotation"));
    }

    [TestMethod]
    public void ExportToXspf_EmptyCollectionIsStillValid()
    {
        XDocument doc = XDocument.Parse(FavoriteTrackExportService.ExportToXspf([]));
        XNamespace ns = "http://xspf.org/ns/0/";

        Assert.IsFalse(doc.Root!.Element(ns + "trackList")!.Elements(ns + "track").Any());
    }

    [TestMethod]
    public void Export_SelectsFormatByExtension()
    {
        List<FavoriteTrack> tracks = [Track("Nils Frahm", "Says")];

        Assert.IsTrue(FavoriteTrackExportService.Export(tracks, ".xspf").StartsWith("<?xml", StringComparison.Ordinal));
        Assert.IsTrue(FavoriteTrackExportService.Export(tracks, ".CSV").StartsWith("Title,Artist", StringComparison.Ordinal));
        Assert.IsTrue(FavoriteTrackExportService.Export(tracks, ".unknown").StartsWith("Title,Artist", StringComparison.Ordinal));
    }

    [TestMethod]
    public void IsArchived_ReflectsExportedAt()
    {
        Assert.IsFalse(Track("A", "B").IsArchived);
        Assert.IsTrue(Track("A", "B", exportedAt: DateTime.Now).IsArchived);
    }

    [TestMethod]
    public void BuildSuggestedFileName_UsesSortableDate()
    {
        Assert.AreEqual(
            "Traydio Favorites 2026-01-02",
            FavoriteTrackExportService.BuildSuggestedFileName(new DateTime(2026, 1, 2)));
    }
}
