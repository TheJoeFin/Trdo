using System;
using System.Collections.Generic;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Decides which directory entry, if any, describes a saved station.
/// <para>
/// The rule that matters: matching is on the stream URL and nothing else. Falling back to the
/// name when no URL matches would look helpful and occasionally be catastrophic - two stations
/// called "Radio One" in different countries would silently swap genres and countries with each
/// other. A station whose URL the directory does not know simply keeps what it has.
/// </para>
/// </summary>
public static class StationMetadataMatchPolicy
{
    /// <summary>
    /// A directory entry chosen for a station, and whether the choice was unambiguous.
    /// </summary>
    /// <param name="Station">The directory entry.</param>
    /// <param name="IsExact">
    /// True when exactly one entry matched the URL. False when several did and one had to be
    /// picked, which is worth reporting rather than applying silently.
    /// </param>
    public readonly record struct MetadataMatch(RadioBrowserStation Station, bool IsExact);

    /// <summary>
    /// Picks the directory entry that describes <paramref name="local"/>, or null when none of
    /// the candidates points at the same stream.
    /// </summary>
    public static MetadataMatch? SelectBestMatch(
        RadioStation local,
        IReadOnlyList<RadioBrowserStation>? candidates)
    {
        if (local is null || candidates is null || candidates.Count == 0)
            return null;

        string target = NormalizeStreamUrl(local.StreamUrl);
        if (target.Length == 0)
            return null;

        List<RadioBrowserStation> matches = [];
        foreach (RadioBrowserStation candidate in candidates)
        {
            if (candidate is null)
                continue;

            // Either form counts: the directory stores both what was registered and what it
            // resolved to, and a saved station may have been added from either.
            if (NormalizeStreamUrl(candidate.Url) == target ||
                NormalizeStreamUrl(candidate.UrlResolved) == target)
            {
                matches.Add(candidate);
            }
        }

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
            return new MetadataMatch(matches[0], IsExact: true);

        // Several entries share the stream - usually the same station registered more than
        // once. Prefer the one whose name the user would recognise, then the most popular.
        RadioBrowserStation best = matches[0];
        int bestScore = ScoreAgainstName(best, local.Name);
        for (int i = 1; i < matches.Count; i++)
        {
            int score = ScoreAgainstName(matches[i], local.Name);
            if (score > bestScore || (score == bestScore && matches[i].Votes > best.Votes))
            {
                best = matches[i];
                bestScore = score;
            }
        }

        return new MetadataMatch(best, IsExact: false);
    }

    /// <summary>
    /// Reduces a stream URL to a form two spellings of the same stream share: trimmed,
    /// lower-cased, without a trailing slash.
    /// </summary>
    public static string NormalizeStreamUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        return url.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private static int ScoreAgainstName(RadioBrowserStation candidate, string? localName)
    {
        if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(candidate.Name))
            return 0;

        string a = candidate.Name.Trim();
        string b = localName.Trim();

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }
}
