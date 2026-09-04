using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers the dedupe/baseline decision that gates the song-change popup:
/// it must stay quiet for the very first observation (startup, or the
/// moment the setting is enabled while something is already playing), never
/// fire while disabled, never fire on blank/unchanged text, and otherwise
/// fire exactly once per meaningful change.
/// </summary>
[TestClass]
public sealed class SongChangeAnnouncementPolicyTests
{
    [TestMethod]
    public void FirstObservation_NeverAnnounces_EvenWhenEnabledAndNonBlank()
    {
        // previous == null means "no baseline yet" (app just launched, or the
        // tracker was reset). This must only establish the baseline.
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(null, "Artist - Title", isEnabled: true));
    }

    [TestMethod]
    public void FirstObservationAfterStationStart_AnnouncesImmediately()
    {
        Assert.IsTrue(SongChangeAnnouncementPolicy.ShouldAnnounce(
            null,
            "Artist - Title",
            isEnabled: true,
            isFirstObservationSinceStationStart: true));
    }

    /// <summary>
    /// A start is rarely one clean event. The same track can be reported twice as sources
    /// converge on it — an ICY title first, then the same title carrying album art — and a
    /// stuttering connection re-opens the start window under a track that has already been
    /// announced. Neither may show the same song a second time.
    /// </summary>
    [TestMethod]
    public void RepeatedTextDuringAStutteringStart_DoesNotAnnounceTwice()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(
            "Artist - Title",
            "Artist - Title",
            isEnabled: true,
            isFirstObservationSinceStationStart: true));

        // Whitespace churn around an otherwise identical title is still the same track.
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(
            " Artist - Title ",
            "Artist - Title",
            isEnabled: true,
            isFirstObservationSinceStationStart: true));
    }

    /// <summary>
    /// The stutter guard must not swallow a genuine change: if the station moved on to a new
    /// track while the connection was settling, that track is what the listener is hearing.
    /// </summary>
    [TestMethod]
    public void ARealChangeDuringTheStartWindow_StillAnnounces()
    {
        Assert.IsTrue(SongChangeAnnouncementPolicy.ShouldAnnounce(
            "Artist - Title",
            "Someone Else - Another Title",
            isEnabled: true,
            isFirstObservationSinceStationStart: true));
    }

    [TestMethod]
    public void StationStart_DoesNotOverrideTheEnabledSetting()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(
            null,
            "Artist - Title",
            isEnabled: false,
            isFirstObservationSinceStationStart: true));
    }

    [TestMethod]
    public void StationStart_DoesNotAnnounceBlankMetadata()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(
            null,
            "   ",
            isEnabled: true,
            isFirstObservationSinceStationStart: true));
    }

    [TestMethod]
    public void Disabled_NeverAnnounces_EvenOnAMeaningfulChange()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("Old Song", "New Song", isEnabled: false));
    }

    [TestMethod]
    public void BlankCurrentText_NeverAnnounces()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("Old Song", "", isEnabled: true));
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("Old Song", "   ", isEnabled: true));
    }

    [TestMethod]
    public void BlankPreviousText_DoesNotCountAsAMeaningfulBaseline()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("", "First Song", isEnabled: true));
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("   ", "First Song", isEnabled: true));
    }

    [TestMethod]
    public void UnchangedText_DoesNotReAnnounce()
    {
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("Same Song", "Same Song", isEnabled: true));
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce(" Same Song ", "Same Song", isEnabled: true));
    }

    [TestMethod]
    public void MeaningfulChangeWhileEnabled_Announces()
    {
        Assert.IsTrue(SongChangeAnnouncementPolicy.ShouldAnnounce("Old Song", "New Song", isEnabled: true));
    }

    [TestMethod]
    public void EnablingMidPlayback_DoesNotAnnounceForAlreadyCurrentSong()
    {
        // Simulates: app has been running with the setting disabled, playing
        // "Song A" the whole time. The baseline (previous) already reflects
        // "Song A" because HandleSongChangePopup keeps updating it regardless
        // of the enabled flag. Turning the setting on must not immediately
        // pop up for the song that's already playing.
        Assert.IsFalse(SongChangeAnnouncementPolicy.ShouldAnnounce("Song A", "Song A", isEnabled: true));

        // The next real change after enabling should announce.
        Assert.IsTrue(SongChangeAnnouncementPolicy.ShouldAnnounce("Song A", "Song B", isEnabled: true));
    }
}