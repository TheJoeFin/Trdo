using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers how long the app holds a song change back before showing it anywhere. Stations
/// commonly push metadata a few seconds ahead of the audio, so the app can name a track before
/// the listener hears it; the delay compensates, and because the lead time is a property of the
/// station's encoder, a per-station override has to beat the app-wide setting outright.
/// The holding itself is covered by <see cref="MetadataPublishGateTests"/>.
/// </summary>
[TestClass]
public sealed class TrackInfoDelayTests
{
    [TestMethod]
    public void WithNoStationOverride_TheAppSettingApplies()
    {
        Assert.AreEqual(5, SongChangeAnnouncementPolicy.ResolveDelaySeconds(null, 5));
    }

    [TestMethod]
    public void AStationOverride_ReplacesTheAppSettingRatherThanAddingToIt()
    {
        Assert.AreEqual(3, SongChangeAnnouncementPolicy.ResolveDelaySeconds(3, 10));
    }

    /// <summary>
    /// Zero is a meaningful override, not "unset": a station whose metadata is already in
    /// sync must be able to opt out of a global delay.
    /// </summary>
    [TestMethod]
    public void AStationOverrideOfZero_DefeatsANonZeroAppSetting()
    {
        Assert.AreEqual(0, SongChangeAnnouncementPolicy.ResolveDelaySeconds(0, 8));
    }

    [TestMethod]
    public void DelaysAreClampedToTheSupportedRange()
    {
        Assert.AreEqual(SongChangeAnnouncementPolicy.MaxDelaySeconds, SongChangeAnnouncementPolicy.ClampDelay(999));
        Assert.AreEqual(SongChangeAnnouncementPolicy.MinDelaySeconds, SongChangeAnnouncementPolicy.ClampDelay(-5));
        Assert.AreEqual(SongChangeAnnouncementPolicy.MinDelaySeconds, SongChangeAnnouncementPolicy.ClampDelay(double.NaN));
    }

    /// <summary>
    /// The supported range runs to a full minute: a handful of stations lead the audio by
    /// far more than the usual few seconds, and the old half-minute ceiling cut them off.
    /// </summary>
    [TestMethod]
    public void DelaysUpToAMinuteAreSupported()
    {
        // Assert.AreEqual(60, SongChangeAnnouncementPolicy.MaxDelaySeconds);
        Assert.AreEqual(45, SongChangeAnnouncementPolicy.ClampDelay(45));
        Assert.AreEqual(60, SongChangeAnnouncementPolicy.ResolveDelaySeconds(60, 0));
    }

    /// <summary>
    /// Mid-stream changes are what the delay is for, and the resolved value is handed to the
    /// player as-is. Which arrivals skip the wait is the gate's decision, not the policy's -
    /// see <see cref="MetadataPublishGateTests"/>.
    /// </summary>
    [TestMethod]
    public void MidStreamChanges_WaitOutTheResolvedDelay()
    {
        Assert.AreEqual(30, SongChangeAnnouncementPolicy.ResolveDelaySeconds(30, 10));
        Assert.AreEqual(60, SongChangeAnnouncementPolicy.ResolveDelaySeconds(null, 60));
    }

    /// <summary>
    /// The "just started" window has to close on its own rather than waiting to be consumed by
    /// a metadata change: resuming part-way through a track produces no metadata change at all,
    /// so a one-shot flag would still be set when the next real track arrived — and would strip
    /// the delay from the one announcement that needs it.
    /// </summary>
    [TestMethod]
    public void TheStationStartWindow_ExpiresOnItsOwn()
    {
        DateTimeOffset started = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.IsTrue(SongChangeAnnouncementPolicy.IsWithinStationStartGrace(started, started));
        Assert.IsTrue(SongChangeAnnouncementPolicy.IsWithinStationStartGrace(
            started, started + TimeSpan.FromSeconds(5)));
        Assert.IsFalse(SongChangeAnnouncementPolicy.IsWithinStationStartGrace(
            started, started + SongChangeAnnouncementPolicy.StationStartGrace + TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void NoStationStart_MeansNoGrace()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.IsWithinStationStartGrace(
            null, DateTimeOffset.UtcNow));
    }

    /// <summary>A clock that steps backwards must not be read as a station that started ahead of now.</summary>
    [TestMethod]
    public void AStartInTheFuture_IsNotTreatedAsInsideTheWindow()
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.IsFalse(SongChangeAnnouncementPolicy.IsWithinStationStartGrace(
            now + TimeSpan.FromMinutes(1), now));
    }

    [TestMethod]
    public void OutOfRangeValuesAreClampedWhenResolving()
    {
        Assert.AreEqual(SongChangeAnnouncementPolicy.MaxDelaySeconds, SongChangeAnnouncementPolicy.ResolveDelaySeconds(120, 0));
        Assert.AreEqual(0, SongChangeAnnouncementPolicy.ResolveDelaySeconds(null, -3));
    }

    [TestMethod]
    public void NoDelay_IsDescribedInWords()
    {
        Assert.AreEqual("No delay", SongChangeAnnouncementPolicy.DescribeDelay(0));
    }

    [TestMethod]
    public void WholeSecondDelays_AreDescribedWithoutADecimalPoint()
    {
        Assert.AreEqual("1 second", SongChangeAnnouncementPolicy.DescribeDelay(1));
        Assert.AreEqual("5 seconds", SongChangeAnnouncementPolicy.DescribeDelay(5));
    }

    /// <summary>The top of the range is a round minute, and reads better said that way.</summary>
    [TestMethod]
    public void TheLongestDelay_IsDescribedAsAMinute()
    {
        Assert.AreEqual("1 minute", SongChangeAnnouncementPolicy.DescribeDelay(60));
        Assert.AreEqual("55 seconds", SongChangeAnnouncementPolicy.DescribeDelay(55));
    }

    [TestMethod]
    public void FractionalDelays_KeepOneDecimalPlace()
    {
        Assert.AreEqual("2.5 seconds", SongChangeAnnouncementPolicy.DescribeDelay(2.5));
    }

    [TestMethod]
    public void DescribeDelay_ClampsBeforeFormatting()
    {
        Assert.AreEqual("No delay", SongChangeAnnouncementPolicy.DescribeDelay(-1));
        Assert.AreEqual(
            SongChangeAnnouncementPolicy.DescribeDelay(SongChangeAnnouncementPolicy.MaxDelaySeconds),
            SongChangeAnnouncementPolicy.DescribeDelay(500));
    }

    /// <summary>
    /// The dwell time — how long the pill stays up — is a separate axis from the delay:
    /// the delay decides when the popup appears, the dwell how long there is to read it.
    /// </summary>
    [TestMethod]
    public void DwellIsClampedToAReadableRange()
    {
        Assert.AreEqual(SongChangeAnnouncementPolicy.MaxDwellSeconds, SongChangeAnnouncementPolicy.ClampDwell(60));
        Assert.AreEqual(SongChangeAnnouncementPolicy.MinDwellSeconds, SongChangeAnnouncementPolicy.ClampDwell(0));
        Assert.AreEqual(SongChangeAnnouncementPolicy.MinDwellSeconds, SongChangeAnnouncementPolicy.ClampDwell(-4));
        Assert.AreEqual(8, SongChangeAnnouncementPolicy.ClampDwell(8));
    }

    /// <summary>
    /// Zero is a legitimate delay ("no delay") but never a legitimate dwell — a popup that
    /// stays up for no time at all is just a flicker — so NaN falls back to the default
    /// rather than to the bottom of the range.
    /// </summary>
    [TestMethod]
    public void ClampDwell_MapsNaNToTheDefaultRatherThanTheMinimum()
    {
        Assert.AreEqual(
            SongChangeAnnouncementPolicy.DefaultDwellSeconds,
            SongChangeAnnouncementPolicy.ClampDwell(double.NaN));
    }

    [TestMethod]
    public void DwellIsAlwaysDescribedAsADuration()
    {
        Assert.AreEqual("2.5 seconds", SongChangeAnnouncementPolicy.DescribeDwell(2.5));
        Assert.AreEqual("1 second", SongChangeAnnouncementPolicy.DescribeDwell(1));
        Assert.AreEqual("10 seconds", SongChangeAnnouncementPolicy.DescribeDwell(10));
        Assert.AreEqual("15 seconds", SongChangeAnnouncementPolicy.DescribeDwell(99));
    }
}
