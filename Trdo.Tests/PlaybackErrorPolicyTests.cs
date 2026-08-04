using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Services;

namespace Trdo.Tests;

/// <summary>
/// Covers the decision that keeps a playback error off the screen when it no longer
/// describes reality. The bug that motivated this: a station played perfectly well,
/// but a failure reported during the engine fallback sat queued against the hidden
/// tray popup and appeared as "Couldn't play this station" the next time the flyout
/// was opened. So the rules under test are mostly about the gap between a failure
/// being reported and it reaching the user.
/// </summary>
[TestClass]
public sealed class PlaybackErrorPolicyTests
{
    /// <summary>
    /// A freshly reported failure on a stopped player with a visible window: the
    /// straightforward case that must still show. Individual tests vary from here.
    /// </summary>
    private static PlaybackErrorSignals Baseline() => new()
    {
        IsPlaying = false,
        IsBuffering = false,
        IsHostVisible = true,
        StreamChangedSinceReport = false,
        AgeSeconds = 0.5,
        IsAudioMonitorRunning = false,
        SecondsSinceAudioHeard = null,
    };

    [TestMethod]
    public void GenuineFailureOnAVisibleWindow_IsShown()
    {
        Assert.AreEqual(PlaybackErrorVerdict.Show, PlaybackErrorPolicy.Evaluate(Baseline()));
    }

    [TestMethod]
    public void PlayingByTheTimeItWouldBeShown_IsDiscarded()
    {
        // The fallback engine got there first. Reporting the failure now would
        // contradict the audio the user is listening to.
        PlaybackErrorSignals signals = Baseline() with { IsPlaying = true };

        Assert.AreEqual(PlaybackErrorVerdict.Discard, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void PlayingWithTheLoopbackHearingAudio_IsDiscarded()
    {
        PlaybackErrorSignals signals = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = true,
            SecondsSinceAudioHeard = 0.2,
        };

        Assert.AreEqual(PlaybackErrorVerdict.Discard, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void PlayingButSilentForLongerThanTheFreshnessWindow_IsStillShown()
    {
        // The engine claiming to play while the speakers stay silent is exactly the
        // failure this error path exists for, so the loopback overrides IsPlaying.
        PlaybackErrorSignals signals = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = true,
            SecondsSinceAudioHeard = PlaybackErrorPolicy.AudioFreshnessSeconds + 5,
            AgeSeconds = PlaybackErrorPolicy.AudioFreshnessSeconds + 5,
        };

        Assert.AreEqual(PlaybackErrorVerdict.Show, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void PlayingWithTheMonitorRunningButNothingHeardYet_IsGivenTheGracePeriod()
    {
        // Monitor just started and no frame has cleared the threshold. Within the
        // grace period that is "too early to tell", not "silent".
        PlaybackErrorSignals tooEarlyToTell = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = true,
            SecondsSinceAudioHeard = null,
            AgeSeconds = PlaybackErrorPolicy.AudioFreshnessSeconds - 1,
        };

        Assert.AreEqual(PlaybackErrorVerdict.Discard, PlaybackErrorPolicy.Evaluate(tooEarlyToTell));

        PlaybackErrorSignals silentLongEnough = tooEarlyToTell with
        {
            AgeSeconds = PlaybackErrorPolicy.AudioFreshnessSeconds + 1,
        };

        Assert.AreEqual(PlaybackErrorVerdict.Show, PlaybackErrorPolicy.Evaluate(silentLongEnough));
    }

    [TestMethod]
    public void MonitorNotRunning_LeavesTheEngineAsTheOnlyWitness()
    {
        // Watchdog off, or playback not started: no level updates arrive, and their
        // absence must not be read as silence.
        PlaybackErrorSignals signals = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = false,
            SecondsSinceAudioHeard = null,
            AgeSeconds = 60,
        };

        Assert.IsTrue(PlaybackErrorPolicy.IsPlaybackHealthy(signals));
    }

    [TestMethod]
    public void StaleAudioFromAPreviousSession_DoesNotCountAsHealthy()
    {
        PlaybackErrorSignals signals = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = true,
            SecondsSinceAudioHeard = 600,
        };

        Assert.IsFalse(PlaybackErrorPolicy.IsPlaybackHealthy(signals));
    }

    [TestMethod]
    public void StreamChangedSinceTheReport_IsDiscarded()
    {
        // The user moved to another station. An error about the last one is noise
        // whatever the current stream is doing.
        PlaybackErrorSignals signals = Baseline() with { StreamChangedSinceReport = true };

        Assert.AreEqual(PlaybackErrorVerdict.Discard, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void HiddenWindow_HoldsRatherThanShowing()
    {
        // The reported bug: shown here, the dialog waits on the hidden popup and
        // ambushes the user whenever they next open the flyout.
        PlaybackErrorSignals signals = Baseline() with { IsHostVisible = false };

        Assert.AreEqual(PlaybackErrorVerdict.Hold, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void HeldErrorExpires_RatherThanWaitingForeverForAWindow()
    {
        PlaybackErrorSignals signals = Baseline() with
        {
            IsHostVisible = false,
            AgeSeconds = PlaybackErrorPolicy.MaxAgeSeconds + 1,
        };

        Assert.AreEqual(PlaybackErrorVerdict.Discard, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void ExpiryOutlivesTheTimeItTakesToOpenTheFlyoutAfterAFailedTrayClick()
    {
        // Playing from the tray opens no window, so the user's way of finding out why
        // nothing happened is to open the flyout. That has to still show the error.
        PlaybackErrorSignals signals = Baseline() with { AgeSeconds = 5 };

        Assert.AreEqual(PlaybackErrorVerdict.Show, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void BufferingRetry_HoldsUntilItsOutcomeIsKnown()
    {
        PlaybackErrorSignals signals = Baseline() with { IsBuffering = true };

        Assert.AreEqual(PlaybackErrorVerdict.Hold, PlaybackErrorPolicy.Evaluate(signals));
    }

    [TestMethod]
    public void ShownError_IsWithdrawnOncePlaybackRecovers()
    {
        PlaybackErrorSignals signals = Baseline() with
        {
            IsPlaying = true,
            IsAudioMonitorRunning = true,
            SecondsSinceAudioHeard = 0.1,
        };

        Assert.IsTrue(PlaybackErrorPolicy.ShouldWithdraw(signals));
    }

    [TestMethod]
    public void ShownError_IsWithdrawnWhenTheUserSwitchesStation()
    {
        Assert.IsTrue(PlaybackErrorPolicy.ShouldWithdraw(Baseline() with { StreamChangedSinceReport = true }));
    }

    [TestMethod]
    public void ShownError_IsNotWithdrawnJustForGettingOld()
    {
        // Once it is on screen it belongs to the user to dismiss.
        PlaybackErrorSignals signals = Baseline() with { AgeSeconds = PlaybackErrorPolicy.MaxAgeSeconds * 10 };

        Assert.IsFalse(PlaybackErrorPolicy.ShouldWithdraw(signals));
    }

    [TestMethod]
    public void ShownError_SurvivesARetryThatIsStillBuffering()
    {
        // Buffering holds an unshown error back, but must not pull a shown one away:
        // the retry may yet fail, and flickering the dialog would be worse either way.
        Assert.IsFalse(PlaybackErrorPolicy.ShouldWithdraw(Baseline() with { IsBuffering = true }));
    }
}
