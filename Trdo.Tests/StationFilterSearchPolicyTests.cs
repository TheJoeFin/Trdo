using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers how the station search picks which countries, languages and genres to offer. The
/// directory carries hundreds of each, far too many to scroll in a dropdown, so a value is
/// reached by typing part of it — which only works if the value the user meant lands at the top
/// of a short list.
/// </summary>
[TestClass]
public sealed class StationFilterSearchPolicyTests
{
    private static readonly StationFilterOption Germany =
        new(StationFilterFacet.Country, "Germany", 4000);

    private static readonly StationFilterOption Niger =
        new(StationFilterFacet.Country, "Niger", 12);

    private static readonly StationFilterOption German =
        new(StationFilterFacet.Language, "german", 3500);

    private static readonly StationFilterOption Rock =
        new(StationFilterFacet.Genre, "rock", 9000);

    private static readonly StationFilterOption ClassicRock =
        new(StationFilterFacet.Genre, "classic rock", 2000);

    private static readonly StationFilterOption PunkRock =
        new(StationFilterFacet.Genre, "punkrock", 300);

    private static readonly StationFilterOption Jazz =
        new(StationFilterFacet.Genre, "jazz", 1500);

    private static List<StationFilterOption> AllOptions() =>
        [Germany, Niger, German, Rock, ClassicRock, PunkRock, Jazz];

    [TestMethod]
    public void AValueStartingWithTheQuery_OutranksOneMatchingLater()
    {
        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(AllOptions(), "ger").ToList();

        // "Germany" and "german" both start with it; "Niger" only contains it.
        Assert.AreEqual(Niger, suggestions[^1]);
        CollectionAssert.AreEquivalent(
            new[] { Germany, German },
            suggestions.Take(2).ToArray());
    }

    /// <summary>
    /// A match at the start of a later word is a near miss, not an incidental one: someone
    /// typing "rock" means rock, then classic rock, and only then punkrock.
    /// </summary>
    [TestMethod]
    public void AWordStartMatch_OutranksAMatchInsideAWord()
    {
        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(AllOptions(), "rock").ToList();

        CollectionAssert.AreEqual(
            new[] { Rock, ClassicRock, PunkRock },
            suggestions);
    }

    [TestMethod]
    public void WithinATier_TheBusierValueComesFirst()
    {
        List<StationFilterOption> options =
        [
            new(StationFilterFacet.Genre, "pop rock", 50),
            new(StationFilterFacet.Genre, "pop", 8000),
        ];

        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(options, "pop").ToList();

        Assert.AreEqual("pop", suggestions[0].Value);
    }

    [TestMethod]
    public void MatchingIsCaseInsensitive()
    {
        Assert.IsTrue(
            StationFilterSearchPolicy.Suggest(AllOptions(), "JAZZ").Contains(Jazz));
    }

    /// <summary>
    /// An empty box opens on the busiest values of each facet rather than on nothing, so the
    /// panel is browsable by someone who does not yet know what they are looking for.
    /// </summary>
    [TestMethod]
    public void WithNothingTyped_TheBusiestValuesOfEachFacetAreOffered()
    {
        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(AllOptions(), string.Empty).ToList();

        Assert.IsTrue(suggestions.Any(option => option.Facet == StationFilterFacet.Country));
        Assert.IsTrue(suggestions.Any(option => option.Facet == StationFilterFacet.Language));
        Assert.IsTrue(suggestions.Any(option => option.Facet == StationFilterFacet.Genre));

        // Countries lead, and the busiest country leads them.
        Assert.AreEqual(Germany, suggestions[0]);
    }

    [TestMethod]
    public void BrowsingIsCappedPerFacet()
    {
        List<StationFilterOption> manyGenres = Enumerable
            .Range(0, 40)
            .Select(i => new StationFilterOption(StationFilterFacet.Genre, $"genre{i}", i))
            .ToList();

        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(manyGenres, null).ToList();

        Assert.AreEqual(StationFilterSearchPolicy.BrowseSuggestionsPerFacet, suggestions.Count);
    }

    [TestMethod]
    public void ATypedQueryReturnsAtMostTheSuggestionCap()
    {
        List<StationFilterOption> manyGenres = Enumerable
            .Range(0, 200)
            .Select(i => new StationFilterOption(StationFilterFacet.Genre, $"rock {i}", i))
            .ToList();

        Assert.AreEqual(
            StationFilterSearchPolicy.MaxSuggestions,
            StationFilterSearchPolicy.Suggest(manyGenres, "rock").Count);
    }

    /// <summary>
    /// An applied filter is already visible as a chip; offering it again wastes a row and
    /// invites a click that does nothing.
    /// </summary>
    [TestMethod]
    public void AlreadyAppliedFiltersAreNotSuggestedAgain()
    {
        List<StationFilterOption> suggestions =
            StationFilterSearchPolicy.Suggest(AllOptions(), "rock", [Rock]).ToList();

        Assert.IsFalse(suggestions.Contains(Rock));
        Assert.IsTrue(suggestions.Contains(ClassicRock));
    }

    /// <summary>
    /// The directory is inconsistent about tag casing, so an applied "Jazz" has to suppress a
    /// suggested "jazz" — they are the same filter.
    /// </summary>
    [TestMethod]
    public void ExclusionOfAppliedFiltersIgnoresCase()
    {
        List<StationFilterOption> suggestions = StationFilterSearchPolicy
            .Suggest(AllOptions(), "jazz", [new StationFilterOption(StationFilterFacet.Genre, "JAZZ", 1500)])
            .ToList();

        Assert.AreEqual(0, suggestions.Count);
    }

    [TestMethod]
    public void BlankValuesAreNeverSuggested()
    {
        List<StationFilterOption> options =
        [
            new(StationFilterFacet.Country, "   ", 99999),
            Germany,
        ];

        CollectionAssert.AreEqual(
            new[] { Germany },
            StationFilterSearchPolicy.Suggest(options, null).ToArray());
    }

    // Applying a pick ----------------------------------------------------

    /// <summary>
    /// The directory's search endpoint takes a list of tags but only one country, so genres
    /// stack up while a second country has to displace the first.
    /// </summary>
    [TestMethod]
    public void GenresAccumulate()
    {
        IReadOnlyList<StationFilterOption> applied =
            StationFilterSearchPolicy.Apply([Rock], Jazz);

        CollectionAssert.AreEqual(new[] { Rock, Jazz }, applied.ToArray());
    }

    [TestMethod]
    public void ASecondCountryReplacesTheFirst()
    {
        IReadOnlyList<StationFilterOption> applied =
            StationFilterSearchPolicy.Apply([Germany, Jazz], Niger);

        CollectionAssert.AreEqual(new[] { Jazz, Niger }, applied.ToArray());
    }

    [TestMethod]
    public void ASecondLanguageReplacesTheFirst()
    {
        StationFilterOption french = new(StationFilterFacet.Language, "french", 900);

        IReadOnlyList<StationFilterOption> applied =
            StationFilterSearchPolicy.Apply([German], french);

        CollectionAssert.AreEqual(new[] { french }, applied.ToArray());
    }

    [TestMethod]
    public void ApplyingTheSameFilterTwice_DoesNotDuplicateIt()
    {
        IReadOnlyList<StationFilterOption> applied =
            StationFilterSearchPolicy.Apply([Rock, Jazz], Jazz);

        CollectionAssert.AreEqual(new[] { Rock, Jazz }, applied.ToArray());
    }

    [TestMethod]
    public void OnlyCountryAndLanguageAreSingleValued()
    {
        Assert.IsTrue(StationFilterSearchPolicy.IsSingleValued(StationFilterFacet.Country));
        Assert.IsTrue(StationFilterSearchPolicy.IsSingleValued(StationFilterFacet.Language));
        Assert.IsFalse(StationFilterSearchPolicy.IsSingleValued(StationFilterFacet.Genre));
    }
}
