using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers the view sorts.
/// <para>
/// Two properties do most of the work and are easy to lose in a refactor. The sort is stable,
/// which is what silently supplies the tie-break: stations that match on the sort key stay in
/// the order the user arranged them, with no position index to maintain. And a missing key
/// sorts last rather than first, so a station with no genre does not head the genre view.
/// </para>
/// </summary>
[TestClass]
public sealed class StationSortPolicyTests
{
    private static RadioStation Station(
        string name,
        string? tags = null,
        string? country = null,
        DateTimeOffset? added = null) => new()
        {
            Id = name,
            Name = name,
            StreamUrl = $"http://example.com/{name}",
            Tags = tags,
            Country = country,
            DateAdded = added
        };

    private static List<string> NamesOf(IReadOnlyList<RadioStation> stations)
    {
        List<string> names = [];
        foreach (RadioStation station in stations)
            names.Add(station.Name);
        return names;
    }

    [TestMethod]
    public void Manual_ReturnsTheInputUntouched()
    {
        List<RadioStation> stations = [Station("Zulu"), Station("Alpha")];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Manual);

        CollectionAssert.AreEqual(new[] { "Zulu", "Alpha" }, NamesOf(sorted));
    }

    [TestMethod]
    public void Name_SortsAlphabeticallyIgnoringCase()
    {
        List<RadioStation> stations = [Station("Banana"), Station("apple"), Station("Cherry")];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Name);

        CollectionAssert.AreEqual(new[] { "apple", "Banana", "Cherry" }, NamesOf(sorted));
    }

    [TestMethod]
    public void Name_PlacesAccentedNamesWhereAReaderExpects()
    {
        // Ordinal comparison would drop "Ärger" after "Zulu"; the invariant culture keeps it
        // with the As, and being invariant rather than current-culture keeps this test - and
        // the app - from depending on the machine's locale.
        List<RadioStation> stations = [Station("Zulu"), Station("Ärger"), Station("Alpha")];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Name);

        CollectionAssert.AreEqual(new[] { "Alpha", "Ärger", "Zulu" }, NamesOf(sorted));
    }

    [TestMethod]
    public void Genre_UsesTheFirstTag_AndPutsStationsWithoutOneLast()
    {
        List<RadioStation> stations =
        [
            Station("NoTags"),
            Station("Rock", tags: "rock,classic"),
            Station("Blank", tags: "   "),
            Station("Ambient", tags: "ambient,chill")
        ];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Genre);

        CollectionAssert.AreEqual(new[] { "Ambient", "Rock", "NoTags", "Blank" }, NamesOf(sorted));
    }

    [TestMethod]
    public void Country_PutsStationsWithoutOneLast()
    {
        List<RadioStation> stations =
        [
            Station("Unknown"),
            Station("German", country: "Germany"),
            Station("Austrian", country: "Austria")
        ];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Country);

        CollectionAssert.AreEqual(new[] { "Austrian", "German", "Unknown" }, NamesOf(sorted));
    }

    [TestMethod]
    public void RecentlyAdded_IsNewestFirst_WithUndatedStationsLastInManualOrder()
    {
        // Stations saved before DateAdded existed have no value. They sort last and, thanks to
        // the sort being stable, keep the user's own order among themselves - a fair stand-in
        // for "these are the old ones".
        DateTimeOffset day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);
        List<RadioStation> stations =
        [
            Station("LegacyA"),
            Station("Older", added: day(1)),
            Station("LegacyB"),
            Station("Newest", added: day(9))
        ];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.RecentlyAdded);

        CollectionAssert.AreEqual(new[] { "Newest", "Older", "LegacyA", "LegacyB" }, NamesOf(sorted));
    }

    [TestMethod]
    public void TiesFallBackToTheManualOrder()
    {
        // The stability of OrderBy is the tie-break. If someone swaps in an unstable sort this
        // is the test that notices.
        List<RadioStation> stations =
        [
            Station("Third", country: "Germany"),
            Station("First", country: "Germany"),
            Station("Second", country: "Germany")
        ];

        IReadOnlyList<RadioStation> sorted = StationSortPolicy.Sort(stations, StationSortMode.Country);

        CollectionAssert.AreEqual(new[] { "Third", "First", "Second" }, NamesOf(sorted));
    }

    [TestMethod]
    public void Sort_HandlesAnEmptyList()
    {
        Assert.AreEqual(0, StationSortPolicy.Sort([], StationSortMode.Name).Count);
    }

    [TestMethod]
    public void HintText_IsEmptyForManual_AndNamesAllThreeSurprisesOtherwise()
    {
        Assert.AreEqual(string.Empty, StationSortPolicy.HintText(StationSortMode.Manual));

        string hint = StationSortPolicy.HintText(StationSortMode.Genre);
        StringAssert.Contains(hint, "Genre");
        StringAssert.Contains(hint, "groups hidden");
        StringAssert.Contains(hint, "reordering off");
    }

    [TestMethod]
    public void DisplayName_CoversEveryMode()
    {
        foreach (StationSortMode mode in Enum.GetValues<StationSortMode>())
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(StationSortPolicy.DisplayName(mode)),
                $"{mode} has no menu label.");
        }
    }
}
