using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers the view groupings ("Group by").
/// <para>
/// The load-bearing properties mirror <see cref="StationSortPolicyTests"/>: a station with no
/// value for the field goes in a labelled bucket at the end rather than being dropped, and
/// folder identity survives a rebuild - via the cache - so a folder the user collapsed does not
/// spring back open the next time a station is added or removed.
/// </para>
/// </summary>
[TestClass]
public sealed class StationGroupingPolicyTests
{
    private static RadioStation Station(string name, string? tags = null, string? country = null) => new()
    {
        Id = name,
        Name = name,
        StreamUrl = $"http://example.com/{name}",
        Tags = tags,
        Country = country
    };

    private static List<string> HeaderNamesOf(List<object> rows)
    {
        List<string> names = [];
        foreach (object row in rows)
        {
            if (row is StationGroup group)
                names.Add(group.Name);
        }
        return names;
    }

    [TestMethod]
    public void None_ReturnsNoRows()
    {
        List<RadioStation> stations = [Station("Alpha", tags: "rock")];

        List<object> rows = StationGroupingPolicy.Flatten(stations, StationGroupByMode.None, []);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void Flatten_HandlesAnEmptyList()
    {
        Assert.IsEmpty(StationGroupingPolicy.Flatten([], StationGroupByMode.Genre, []));
    }

    [TestMethod]
    public void Genre_BucketsStations_AndPutsStationsWithoutOneLast()
    {
        List<RadioStation> stations =
        [
            Station("NoTags"),
            Station("Rock1", tags: "rock,classic"),
            Station("Ambient1", tags: "ambient,chill"),
            Station("Rock2", tags: "rock")
        ];

        List<object> rows = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, []);

        Assert.AreSequenceEqual(["ambient", "rock", "No genre"], HeaderNamesOf(rows));
    }

    [TestMethod]
    public void Country_PutsStationsWithoutOneLast_LabelledNoCountry()
    {
        List<RadioStation> stations =
        [
            Station("Unknown"),
            Station("German", country: "Germany"),
            Station("Austrian", country: "Austria")
        ];

        List<object> rows = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Country, []);

        Assert.AreSequenceEqual(["Austria", "Germany", "No country"], HeaderNamesOf(rows));
    }

    [TestMethod]
    public void BucketingIsCaseInsensitive()
    {
        List<RadioStation> stations =
        [
            Station("A", tags: "Rock"),
            Station("B", tags: "rock")
        ];

        List<object> rows = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, []);

        List<StationGroup> groups = [];
        foreach (object row in rows)
        {
            if (row is StationGroup group)
                groups.Add(group);
        }

        Assert.HasCount(1, groups);
        Assert.AreEqual(2, groups[0].StationCount);
    }

    [TestMethod]
    public void EachRowIsAFolderFollowedByItsStations_WhenExpanded()
    {
        List<RadioStation> stations = [Station("Rock1", tags: "rock"), Station("Rock2", tags: "rock")];

        List<object> rows = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, []);

        Assert.HasCount(3, rows);
        Assert.IsTrue(rows[0] is StationGroup);
        Assert.AreSame(stations[0], rows[1]);
        Assert.AreSame(stations[1], rows[2]);
    }

    [TestMethod]
    public void CollapsedFolder_ContributesOnlyItsHeader()
    {
        Dictionary<string, StationGroup> cache = [];
        List<RadioStation> stations = [Station("Rock1", tags: "rock")];

        List<object> first = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, cache);
        ((StationGroup)first[0]).IsExpanded = false;

        List<object> second = StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, cache);

        Assert.HasCount(1, second);
    }

    [TestMethod]
    public void SameBucket_ReusesTheFolderInstanceAcrossRebuilds()
    {
        // Load-bearing for expand/collapse state: a rebuild that manufactured a new StationGroup
        // every time would silently re-expand every folder the user had just collapsed.
        Dictionary<string, StationGroup> cache = [];
        List<RadioStation> stations = [Station("Rock1", tags: "rock")];

        StationGroup first = (StationGroup)StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, cache)[0];
        StationGroup second = (StationGroup)StationGroupingPolicy.Flatten(stations, StationGroupByMode.Genre, cache)[0];

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void EveryFolderIsMarkedVirtual()
    {
        List<object> rows = StationGroupingPolicy.Flatten([Station("A", tags: "rock")], StationGroupByMode.Genre, []);

        Assert.IsTrue(((StationGroup)rows[0]).IsVirtual);
    }

    [TestMethod]
    public void ABucketThatDisappears_IsDroppedFromTheCache()
    {
        Dictionary<string, StationGroup> cache = [];
        StationGroupingPolicy.Flatten([Station("Rock1", tags: "rock"), Station("Jazz1", tags: "jazz")], StationGroupByMode.Genre, cache);
        Assert.HasCount(2, cache);

        // The last jazz station is gone; the jazz folder should not linger in the cache forever.
        StationGroupingPolicy.Flatten([Station("Rock1", tags: "rock")], StationGroupByMode.Genre, cache);

        Assert.HasCount(1, cache);
    }

    [TestMethod]
    public void HintText_IsEmptyForNone_AndNamesTheField()
    {
        Assert.AreEqual(string.Empty, StationGroupingPolicy.HintText(StationGroupByMode.None));

        string hint = StationGroupingPolicy.HintText(StationGroupByMode.Genre);
        StringAssert.Contains(hint, "Genre");
        StringAssert.Contains(hint, "reordering off");
    }

    [TestMethod]
    public void DisplayName_CoversEveryMode()
    {
        foreach (StationGroupByMode mode in Enum.GetValues<StationGroupByMode>())
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(StationGroupingPolicy.DisplayName(mode)),
                $"{mode} has no menu label.");
        }
    }
}
