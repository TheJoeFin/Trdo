using System;
using System.Collections.Generic;
using System.Linq;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Ranks and selects filter suggestions for the station search.
/// <para>
/// The directory offers a couple of hundred countries, a similar number of languages and
/// hundreds of genre tags. That is far too many to scroll in a dropdown, let alone one inside a
/// 320px window — so the way to reach a value is to type part of it, and the job here is to put
/// the value the user meant at the top of a short list. Kept free of WinUI dependencies so the
/// ranking can be tested directly (see Trdo.Tests).
/// </para>
/// </summary>
public static class StationFilterSearchPolicy
{
    /// <summary>Longest suggestion list returned for a typed query.</summary>
    public const int MaxSuggestions = 24;

    /// <summary>
    /// How many values of each facet to show when nothing has been typed. The picker opens on
    /// this list rather than on emptiness, so browsing still works for someone who does not yet
    /// know what they are looking for.
    /// </summary>
    public const int BrowseSuggestionsPerFacet = 5;

    /// <summary>
    /// The order facets appear in while browsing. Country first because it is the most common
    /// way people narrow a radio directory.
    /// </summary>
    private static readonly StationFilterFacet[] BrowseOrder =
    [
        StationFilterFacet.Country,
        StationFilterFacet.Language,
        StationFilterFacet.Genre
    ];

    /// <summary>
    /// Whether a facet can only carry one value at a time. The directory's search endpoint takes
    /// a single country and a single language but a list of tags, so genres accumulate while
    /// picking a second country replaces the first.
    /// </summary>
    public static bool IsSingleValued(StationFilterFacet facet) =>
        facet is StationFilterFacet.Country or StationFilterFacet.Language;

    /// <summary>
    /// Picks the suggestions to offer for what has been typed so far.
    /// </summary>
    /// <param name="allOptions">Every known value across all facets.</param>
    /// <param name="query">What the user has typed; blank means "show me what's there".</param>
    /// <param name="alreadySelected">Applied filters, which are never suggested again.</param>
    public static IReadOnlyList<StationFilterOption> Suggest(
        IEnumerable<StationFilterOption> allOptions,
        string? query,
        IEnumerable<StationFilterOption>? alreadySelected = null)
    {
        HashSet<(StationFilterFacet, string)> selected = BuildSelectedKeys(alreadySelected);

        List<StationFilterOption> candidates = allOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .Where(option => !selected.Contains(KeyOf(option)))
            .ToList();

        string trimmed = query?.Trim() ?? string.Empty;

        return trimmed.Length == 0
            ? Browse(candidates)
            : Rank(candidates, trimmed);
    }

    /// <summary>
    /// The opening list: the busiest few values of each facet, so the panel shows something
    /// worth clicking before a single key is pressed.
    /// </summary>
    private static List<StationFilterOption> Browse(List<StationFilterOption> candidates)
    {
        List<StationFilterOption> results = [];

        foreach (StationFilterFacet facet in BrowseOrder)
        {
            results.AddRange(candidates
                .Where(option => option.Facet == facet)
                .OrderByDescending(option => option.StationCount)
                .ThenBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .Take(BrowseSuggestionsPerFacet));
        }

        return results;
    }

    /// <summary>
    /// Orders matches so that the closer the match is to the start of a value, the higher it
    /// sits; within a tier, the value carrying more stations wins. Typing "ger" should reach
    /// Germany before "Niger", and "rock" should reach rock before "classic rock".
    /// </summary>
    private static List<StationFilterOption> Rank(List<StationFilterOption> candidates, string query)
    {
        return candidates
            .Select(option => (Option: option, Tier: MatchTier(option.Value, query)))
            .Where(match => match.Tier < NoMatch)
            .OrderBy(match => match.Tier)
            .ThenByDescending(match => match.Option.StationCount)
            .ThenBy(match => match.Option.Value, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSuggestions)
            .Select(match => match.Option)
            .ToList();
    }

    private const int NoMatch = 3;

    /// <summary>
    /// 0 when the value starts with the query, 1 when a later word does, 2 for a match anywhere
    /// else, and <see cref="NoMatch"/> when the value does not contain the query at all.
    /// </summary>
    private static int MatchTier(string value, string query)
    {
        int index = value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);

        if (index < 0)
            return NoMatch;

        if (index == 0)
            return 0;

        // Word boundaries in this data are spaces, hyphens and slashes ("hip-hop", "pop/rock").
        char preceding = value[index - 1];
        return preceding is ' ' or '-' or '/' or '_' ? 1 : 2;
    }

    /// <summary>
    /// Works out the applied set after picking a suggestion: single-valued facets replace
    /// whatever they held, multi-valued ones accumulate, and picking the same value twice is a
    /// no-op rather than a duplicate chip.
    /// </summary>
    public static IReadOnlyList<StationFilterOption> Apply(
        IEnumerable<StationFilterOption> current,
        StationFilterOption added)
    {
        List<StationFilterOption> result = current
            .Where(option => !KeyOf(option).Equals(KeyOf(added)))
            .Where(option => !(IsSingleValued(added.Facet) && option.Facet == added.Facet))
            .ToList();

        result.Add(added);
        return result;
    }

    private static HashSet<(StationFilterFacet, string)> BuildSelectedKeys(
        IEnumerable<StationFilterOption>? selected)
    {
        HashSet<(StationFilterFacet, string)> keys = [];

        if (selected is null)
            return keys;

        foreach (StationFilterOption option in selected)
        {
            keys.Add(KeyOf(option));
        }

        return keys;
    }

    /// <summary>
    /// Identity of a filter. The directory is inconsistent about casing on tags ("Jazz" and
    /// "jazz" both appear), so values are compared case-insensitively throughout.
    /// </summary>
    private static (StationFilterFacet Facet, string Value) KeyOf(StationFilterOption option) =>
        (option.Facet, option.Value.Trim().ToLowerInvariant());
}
