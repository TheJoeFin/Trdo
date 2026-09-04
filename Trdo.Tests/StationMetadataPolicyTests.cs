using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers looking a station up in the directory and applying what comes back.
/// <para>
/// Two rules carry the risk here. Matching is on the stream URL only - falling back to the name
/// would silently attach one station's country and genre to a different station that happens to
/// share a name. And the merge never touches the five fields the user owns, so a refresh cannot
/// undo a rename or a volume they tuned by ear.
/// </para>
/// </summary>
[TestClass]
public sealed class StationMetadataPolicyTests
{
    private static RadioStation Local(string name, string url) =>
        new() { Id = name, Name = name, StreamUrl = url };

    private static RadioBrowserStation Remote(
        string name,
        string url = "",
        string urlResolved = "",
        int votes = 0) => new()
        {
            Name = name,
            Url = url,
            UrlResolved = urlResolved,
            Votes = votes,
            StationUuid = $"uuid-{name}",
            Tags = "jazz,blues",
            Country = "Germany",
            CountryCode = "DE",
            Language = "german",
            Codec = "AAC",
            Bitrate = 128,
            Homepage = "http://remote.example",
            Favicon = "http://remote.example/fav.ico"
        };

    // ---------------------------------------------------------------- Matching

    [TestMethod]
    public void SelectBestMatch_SingleUrlMatch_IsExact()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        List<RadioBrowserStation> candidates = [Remote("Jazz FM", url: "http://example.com/jazz")];

        StationMetadataMatchPolicy.MetadataMatch? match =
            StationMetadataMatchPolicy.SelectBestMatch(local, candidates);

        Assert.IsNotNull(match);
        Assert.IsTrue(match.Value.IsExact);
    }

    [TestMethod]
    public void SelectBestMatch_MatchesOnUrlResolved_WhenTheRegisteredUrlDiffers()
    {
        // Stations added from a search are saved with the resolved URL, so this is the common
        // case rather than an edge one.
        RadioStation local = Local("Jazz FM", "http://cdn.example.com/jazz");
        List<RadioBrowserStation> candidates =
        [
            Remote("Jazz FM", url: "http://example.com/jazz", urlResolved: "http://cdn.example.com/jazz")
        ];

        Assert.IsNotNull(StationMetadataMatchPolicy.SelectBestMatch(local, candidates));
    }

    [TestMethod]
    public void SelectBestMatch_IgnoresTrailingSlashAndCaseDifferences()
    {
        RadioStation local = Local("Jazz FM", "HTTP://Example.com/Jazz/");
        List<RadioBrowserStation> candidates = [Remote("Jazz FM", url: "http://example.com/jazz")];

        Assert.IsNotNull(StationMetadataMatchPolicy.SelectBestMatch(local, candidates));
    }

    [TestMethod]
    public void SelectBestMatch_ReturnsNull_WhenNoUrlMatches_EvenOnAnExactNameMatch()
    {
        // The important negative case. Two stations called "Radio One" in different countries
        // would otherwise swap their details with each other.
        RadioStation local = Local("Radio One", "http://uk.example.com/one");
        List<RadioBrowserStation> candidates = [Remote("Radio One", url: "http://it.example.com/uno")];

        Assert.IsNull(StationMetadataMatchPolicy.SelectBestMatch(local, candidates));
    }

    [TestMethod]
    public void SelectBestMatch_SeveralUrlMatches_PrefersTheNameMatchAndFlagsItInexact()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        List<RadioBrowserStation> candidates =
        [
            Remote("Some Relay", url: "http://example.com/jazz", votes: 900),
            Remote("Jazz FM", url: "http://example.com/jazz", votes: 5)
        ];

        StationMetadataMatchPolicy.MetadataMatch? match =
            StationMetadataMatchPolicy.SelectBestMatch(local, candidates);

        Assert.IsNotNull(match);
        Assert.AreEqual("Jazz FM", match.Value.Station.Name, "The name the user recognises beats raw popularity.");
        Assert.IsFalse(match.Value.IsExact, "Several entries shared the stream, which is worth reporting.");
    }

    [TestMethod]
    public void SelectBestMatch_SeveralUrlMatchesAndNoNameMatch_FallsBackToVotes()
    {
        RadioStation local = Local("Something Else", "http://example.com/jazz");
        List<RadioBrowserStation> candidates =
        [
            Remote("Relay A", url: "http://example.com/jazz", votes: 10),
            Remote("Relay B", url: "http://example.com/jazz", votes: 900)
        ];

        StationMetadataMatchPolicy.MetadataMatch? match =
            StationMetadataMatchPolicy.SelectBestMatch(local, candidates);

        Assert.AreEqual("Relay B", match!.Value.Station.Name);
    }

    [TestMethod]
    public void SelectBestMatch_ReturnsNull_ForEmptyInput()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");

        Assert.IsNull(StationMetadataMatchPolicy.SelectBestMatch(local, []));
        Assert.IsNull(StationMetadataMatchPolicy.SelectBestMatch(local, null));
        Assert.IsNull(StationMetadataMatchPolicy.SelectBestMatch(Local("No URL", ""), [Remote("x", url: "")]));
    }

    // ---------------------------------------------------------------- Merging

    [TestMethod]
    public void Merge_FillsInMissingDetails()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");

        Assert.IsTrue(StationMetadataMergePolicy.Merge(local, Remote("Jazz FM"), overwriteExisting: false));

        Assert.AreEqual("jazz,blues", local.Tags);
        Assert.AreEqual("Germany", local.Country);
        Assert.AreEqual("DE", local.CountryCode);
        Assert.AreEqual("german", local.Language);
        Assert.AreEqual("AAC", local.Codec);
        Assert.AreEqual(128, local.Bitrate);
        Assert.AreEqual("uuid-Jazz FM", local.StationUuid);
        Assert.IsNotNull(local.MetadataRefreshedUtc);
    }

    [TestMethod]
    public void Merge_WithoutOverwrite_LeavesDetailsTheStationAlreadyHas()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        local.Country = "Austria";
        local.Tags = "classical";

        StationMetadataMergePolicy.Merge(local, Remote("Jazz FM"), overwriteExisting: false);

        Assert.AreEqual("Austria", local.Country);
        Assert.AreEqual("classical", local.Tags);
        Assert.AreEqual("german", local.Language, "Fields with nothing in them are still filled.");
    }

    [TestMethod]
    public void Merge_NeverTouchesTheFieldsTheUserOwns_EvenWhenOverwriting()
    {
        // A refresh means "tell me what this station is", never "undo my settings".
        RadioStation local = new()
        {
            Id = "id",
            Name = "My Renamed Station",
            StreamUrl = "http://example.com/jazz",
            Volume = 0.42,
            BufferLevel = 2.5,
            SongPopupDelaySeconds = 12
        };

        StationMetadataMergePolicy.Merge(local, Remote("Official Directory Name"), overwriteExisting: true);

        Assert.AreEqual("My Renamed Station", local.Name);
        Assert.AreEqual("http://example.com/jazz", local.StreamUrl);
        Assert.AreEqual(0.42, local.Volume, 0.0001);
        Assert.AreEqual(2.5, local.BufferLevel);
        Assert.AreEqual(12, local.SongPopupDelaySeconds);
    }

    [TestMethod]
    public void Merge_NeverReplacesAChosenHomepageOrFavicon_ButFillsAGap()
    {
        RadioStation chosen = Local("Jazz FM", "http://example.com/jazz");
        chosen.Homepage = "http://my.example";
        chosen.FaviconUrl = "http://my.example/icon.png";

        StationMetadataMergePolicy.Merge(chosen, Remote("Jazz FM"), overwriteExisting: true);

        Assert.AreEqual("http://my.example", chosen.Homepage);
        Assert.AreEqual("http://my.example/icon.png", chosen.FaviconUrl);

        RadioStation blank = Local("Blank", "http://example.com/blank");
        StationMetadataMergePolicy.Merge(blank, Remote("Blank"), overwriteExisting: false);
        Assert.AreEqual("http://remote.example", blank.Homepage);
    }

    [TestMethod]
    public void Merge_PreservesDateAdded()
    {
        // The directory's timestamps describe their record, not when this user added it.
        DateTimeOffset added = new(2020, 5, 5, 0, 0, 0, TimeSpan.Zero);
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        local.DateAdded = added;

        StationMetadataMergePolicy.Merge(local, Remote("Jazz FM"), overwriteExisting: true);

        Assert.AreEqual(added, local.DateAdded);
    }

    [TestMethod]
    public void Merge_ReturnsFalse_WhenNothingChanged()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        StationMetadataMergePolicy.Merge(local, Remote("Jazz FM"), overwriteExisting: false);

        // A no-op refresh must not report a change, or every run would rewrite the station file.
        Assert.IsFalse(StationMetadataMergePolicy.Merge(local, Remote("Jazz FM"), overwriteExisting: false));
    }

    [TestMethod]
    public void Merge_IgnoresBlankValuesFromTheDirectory()
    {
        RadioStation local = Local("Jazz FM", "http://example.com/jazz");
        local.Country = "Austria";

        RadioBrowserStation sparse = new()
        {
            Name = "Jazz FM",
            Url = "http://example.com/jazz",
            Country = "   ",
            Tags = "",
            Bitrate = 0
        };

        StationMetadataMergePolicy.Merge(local, sparse, overwriteExisting: true);

        Assert.AreEqual("Austria", local.Country, "Blank is not a value worth overwriting with.");
        Assert.IsNull(local.Tags);
        Assert.IsNull(local.Bitrate);
    }
}
