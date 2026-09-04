using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers id stamping. Two things matter: an existing id is never rewritten (the layout file
/// and the saved selection both point at it), and a load that changes nothing must report
/// no change, because the caller writes the whole station list back out when it does.
/// </summary>
[TestClass]
public sealed class StationIdentityPolicyTests
{
    private static RadioStation Station(string name, string id = "") =>
        new() { Name = name, StreamUrl = $"http://example.com/{name}", Id = id };

    [TestMethod]
    public void EnsureIds_StampsOnlyStationsWithoutOne()
    {
        RadioStation existing = Station("Keeps", "abc123");
        RadioStation blank = Station("Gets one");
        List<RadioStation> stations = [existing, blank];

        Assert.IsTrue(StationIdentityPolicy.EnsureIds(stations));

        Assert.AreEqual("abc123", existing.Id, "An existing id is referenced elsewhere and must not be rewritten.");
        Assert.AreNotEqual(string.Empty, blank.Id);
    }

    [TestMethod]
    public void EnsureIds_ReturnsFalse_WhenEverythingAlreadyHasAnId()
    {
        List<RadioStation> stations = [Station("A", "id-a"), Station("B", "id-b")];

        Assert.IsFalse(
            StationIdentityPolicy.EnsureIds(stations),
            "Reporting a change here would make every load trigger a needless save.");
    }

    [TestMethod]
    public void EnsureIds_TreatsWhitespaceAsMissing()
    {
        RadioStation station = Station("Whitespace", "   ");

        Assert.IsTrue(StationIdentityPolicy.EnsureIds([station]));
        Assert.IsFalse(string.IsNullOrWhiteSpace(station.Id));
    }

    [TestMethod]
    public void EnsureIds_AssignsDistinctIds()
    {
        List<RadioStation> stations = [Station("A"), Station("B"), Station("C")];

        StationIdentityPolicy.EnsureIds(stations);

        HashSet<string> ids = [];
        foreach (RadioStation station in stations)
            Assert.IsTrue(ids.Add(station.Id), "Ids must be unique - they are the key everything else joins on.");
    }

    [TestMethod]
    public void EnsureIds_ToleratesNull()
    {
        Assert.IsFalse(StationIdentityPolicy.EnsureIds(null));
    }

    [TestMethod]
    public void NewId_IsHexOnly_SoItNeedsNoEscapingAsAKey()
    {
        string id = StationIdentityPolicy.NewId();

        Assert.AreEqual(32, id.Length);
        foreach (char c in id)
            Assert.IsTrue(char.IsAsciiHexDigitLower(c), $"Unexpected character '{c}' in id.");
    }
}
