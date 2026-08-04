using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Guards the on-disk contract for <c>stations.json</c>.
/// <para>
/// This matters more than a normal round-trip test. A pre-2.0 build that cannot parse the
/// file treats it as empty and then overwrites it on quit, so a format change an older build
/// chokes on does not degrade gracefully - it destroys the user's station list. The file must
/// stay a bare JSON array of stations, and every property added since must be optional.
/// </para>
/// </summary>
[TestClass]
public sealed class StationStorageFormatTests
{
    /// <summary>Exactly what a 1.x build wrote: a bare array, PascalCase, four fields plus the overrides.</summary>
    private const string LegacyJson = """
        [
          {"Name":"Jazz FM","StreamUrl":"http://example.com/jazz","Homepage":"http://jazz.example","FaviconUrl":"http://jazz.example/fav.ico","Volume":0.8,"BufferLevel":null,"SongPopupDelaySeconds":null},
          {"Name":"BBC Radio 4","StreamUrl":"http://example.com/r4","Homepage":null,"FaviconUrl":null,"Volume":1,"BufferLevel":2,"SongPopupDelaySeconds":1.5}
        ]
        """;

    [TestMethod]
    public void ParseStations_ReadsJsonWrittenByAPre20Build()
    {
        List<RadioStation> stations = StationStorageFormat.ParseStations(LegacyJson);

        Assert.AreEqual(2, stations.Count);
        Assert.AreEqual("Jazz FM", stations[0].Name);
        Assert.AreEqual("http://example.com/jazz", stations[0].StreamUrl);
        Assert.AreEqual("http://jazz.example", stations[0].Homepage);
        Assert.AreEqual(0.8, stations[0].Volume, 0.0001);
        Assert.AreEqual(2, stations[1].BufferLevel);
        Assert.AreEqual(1.5, stations[1].SongPopupDelaySeconds);

        // Nothing in the old file supplies these; they must come back as "not set" rather
        // than as anything that would look like real data to a sort or a backfill.
        Assert.AreEqual(string.Empty, stations[0].Id);
        Assert.IsNull(stations[0].Tags);
        Assert.IsNull(stations[0].Country);
        Assert.IsNull(stations[0].DateAdded);
    }

    [TestMethod]
    public void SerializeStations_StillWritesABareArray()
    {
        string json = StationStorageFormat.SerializeStations([NewStation()]);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.AreEqual(
            JsonValueKind.Array,
            document.RootElement.ValueKind,
            "Wrapping the file in an object would make older builds discard every station.");
    }

    [TestMethod]
    public void RoundTrip_PreservesEveryPersistedProperty()
    {
        RadioStation original = NewStation();

        List<RadioStation> parsed = StationStorageFormat.ParseStations(
            StationStorageFormat.SerializeStations([original]));

        Assert.AreEqual(1, parsed.Count);
        RadioStation copy = parsed[0];
        Assert.AreEqual(original.Id, copy.Id);
        Assert.AreEqual(original.Name, copy.Name);
        Assert.AreEqual(original.StreamUrl, copy.StreamUrl);
        Assert.AreEqual(original.Homepage, copy.Homepage);
        Assert.AreEqual(original.FaviconUrl, copy.FaviconUrl);
        Assert.AreEqual(original.Volume, copy.Volume, 0.0001);
        Assert.AreEqual(original.BufferLevel, copy.BufferLevel);
        Assert.AreEqual(original.SongPopupDelaySeconds, copy.SongPopupDelaySeconds);
        Assert.AreEqual(original.StationUuid, copy.StationUuid);
        Assert.AreEqual(original.Tags, copy.Tags);
        Assert.AreEqual(original.Country, copy.Country);
        Assert.AreEqual(original.CountryCode, copy.CountryCode);
        Assert.AreEqual(original.Language, copy.Language);
        Assert.AreEqual(original.Codec, copy.Codec);
        Assert.AreEqual(original.Bitrate, copy.Bitrate);
        Assert.AreEqual(original.DateAdded, copy.DateAdded);
        Assert.AreEqual(original.MetadataRefreshedUtc, copy.MetadataRefreshedUtc);
    }

    [TestMethod]
    public void Serialize_OmitsViewState()
    {
        RadioStation station = NewStation();
        station.GroupId = "group-1";
        station.IsSelectedStation = true;

        string json = StationStorageFormat.SerializeStations([station]);

        // Grouping lives in the layout file and selection lives in settings. Writing either
        // here would create a second, silently diverging source of truth.
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("GroupId"));
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("IsSelectedStation"));
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("PrimaryGenre"));
        StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex("TagList"));
    }

    [TestMethod]
    public void ParseStations_IgnoresPropertiesItDoesNotKnow()
    {
        // Forward compatibility: a newer build may add fields, and downgrading must not
        // throw away the file.
        const string futureJson = """
            [{"Name":"Future FM","StreamUrl":"http://example.com/f","SomethingNew":{"nested":true},"AlsoNew":42}]
            """;

        List<RadioStation> stations = StationStorageFormat.ParseStations(futureJson);

        Assert.AreEqual(1, stations.Count);
        Assert.AreEqual("Future FM", stations[0].Name);
    }

    [TestMethod]
    public void ParseStations_ReturnsEmpty_ForEmptyInput()
    {
        Assert.AreEqual(0, StationStorageFormat.ParseStations(null).Count);
        Assert.AreEqual(0, StationStorageFormat.ParseStations("").Count);
        Assert.AreEqual(0, StationStorageFormat.ParseStations("   ").Count);
    }

    [TestMethod]
    public void ParseStations_Throws_OnMalformedJson()
    {
        // Deliberately not swallowed here: only the caller knows whether it is safe to treat
        // an unreadable file as "no stations" and overwrite it.
        Assert.ThrowsException<JsonException>(() => StationStorageFormat.ParseStations("{not json"));
    }

    private static RadioStation NewStation() => new()
    {
        Id = "0123456789abcdef0123456789abcdef",
        Name = "Test FM",
        StreamUrl = "http://example.com/stream",
        Homepage = "http://example.com",
        FaviconUrl = "http://example.com/fav.ico",
        Volume = 1.25,
        BufferLevel = 1.5,
        SongPopupDelaySeconds = 3,
        StationUuid = "uuid-1",
        Tags = "jazz,blues",
        Country = "Germany",
        CountryCode = "DE",
        Language = "german",
        Codec = "AAC",
        Bitrate = 128,
        DateAdded = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        MetadataRefreshedUtc = new DateTimeOffset(2026, 6, 7, 8, 9, 10, TimeSpan.Zero)
    };
}
