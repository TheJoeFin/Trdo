using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers how long the song change popup waits before appearing. Stations commonly push
/// metadata a few seconds ahead of the audio, so the popup can announce a track before the
/// listener hears it; the delay compensates, and because the lead time is a property of the
/// station's encoder, a per-station override has to beat the app-wide setting outright.
/// </summary>
[TestClass]
public sealed class SongChangePopupDelayTests
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
        Assert.AreEqual(60, SongChangeAnnouncementPolicy.MaxDelaySeconds);
        Assert.AreEqual(45, SongChangeAnnouncementPolicy.ClampDelay(45));
        Assert.AreEqual(60, SongChangeAnnouncementPolicy.ResolveDelaySeconds(60, 0));
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
}
