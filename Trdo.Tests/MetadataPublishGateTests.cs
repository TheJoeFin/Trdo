using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Models;
using Trdo.Services.Metadata;

namespace Trdo.Tests;

/// <summary>
/// Covers the hold that keeps a song change from reaching the app before the audio does.
/// Everything the user sees about the current track - the window, the mini player, the media
/// transport controls, the tray tooltip, the playlist history and the popup - is fed from a
/// single publication here, so a track published early or twice is visible in six places at
/// once. The delay's <em>value</em> is resolved elsewhere; see <see cref="TrackInfoDelayTests"/>.
/// </summary>
[TestClass]
public sealed class MetadataPublishGateTests
{
    /// <summary>
    /// Stands in for the thread-pool timer so the tests can step time forward exactly rather
    /// than sleeping. Deliberately keeps firing a cancelled callback available via
    /// <see cref="FireStale"/>: a real timer callback already dispatched cannot be recalled,
    /// and the gate has to survive that.
    /// </summary>
    private sealed class FakeScheduler : IDelayScheduler
    {
        private Action? _callback;
        private Action? _lastScheduled;

        public TimeSpan? ScheduledDelay { get; private set; }

        public bool HasPending => _callback is not null;

        public void Schedule(TimeSpan delay, Action callback)
        {
            ScheduledDelay = delay;
            _callback = callback;
            _lastScheduled = callback;
        }

        public void Cancel()
        {
            ScheduledDelay = null;
            _callback = null;
        }

        /// <summary>Runs the scheduled callback, as the timer elapsing would.</summary>
        public void Fire()
        {
            Action? callback = _callback;
            _callback = null;
            ScheduledDelay = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Runs the most recently scheduled callback even if it has since been cancelled, as a
        /// timer callback that was already dispatched when the cancel arrived would.
        /// </summary>
        public void FireStale() => _lastScheduled?.Invoke();

        /// <summary>Hands back the currently scheduled callback so a test can hold it past a replacement.</summary>
        public Action? CaptureScheduled() => _callback;
    }

    private static StreamMetadata Track(string title) => new() { StreamTitle = title };

    private static (MetadataPublishGate Gate, FakeScheduler Scheduler, List<string> Published) NewGate(
        double delaySeconds,
        Func<DateTimeOffset>? clock = null)
    {
        FakeScheduler scheduler = new();
        MetadataPublishGate gate = new(scheduler, clock);
        List<string> published = [];
        gate.MetadataPublished += (_, metadata) => published.Add(metadata.DisplayText);
        gate.DelaySeconds = delaySeconds;
        return (gate, scheduler, published);
    }

    /// <summary>
    /// The track a station opens with was already playing before the listener tuned in, so its
    /// metadata describes audio arriving right now. Holding it would leave the window blank for
    /// up to a minute after pressing play.
    /// </summary>
    [TestMethod]
    public void TheFirstTrackAfterAStationStarts_PublishesImmediately()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(60);

        gate.Submit(Track("Opening Track"));

        Assert.AreSequenceEqual(["Opening Track"], published);
        Assert.IsFalse(scheduler.HasPending);
        Assert.AreEqual("Opening Track", gate.Current.DisplayText);
    }

    [TestMethod]
    public void AMidStreamChange_IsHeldUntilTheDelayElapses()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));

        gate.Submit(Track("Second Track"));

        Assert.AreSequenceEqual(["Opening Track"], published);
        Assert.AreEqual(TimeSpan.FromSeconds(20), scheduler.ScheduledDelay);

        scheduler.Fire();

        Assert.AreSequenceEqual(["Opening Track", "Second Track"], published);
    }

    /// <summary>
    /// A track superseded during the wait is already over by the time it would be shown, so
    /// showing it would name a song nobody is listening to.
    /// </summary>
    [TestMethod]
    public void ANewerTrackDuringTheHold_ReplacesTheHeldOneAndRestartsTheWait()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));

        gate.Submit(Track("Superseded"));
        gate.Submit(Track("Actual"));
        scheduler.Fire();

        Assert.AreSequenceEqual(["Opening Track", "Actual"], published);
        Assert.DoesNotContain("Superseded", published);
    }

    [TestMethod]
    public void WithNoDelay_EveryChangePublishesImmediately()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(0);

        gate.Submit(Track("First"));
        gate.Submit(Track("Second"));

        Assert.AreSequenceEqual(["First", "Second"], published);
        Assert.IsFalse(scheduler.HasPending);
    }

    /// <summary>
    /// Blank metadata means playback stopped or the station cleared its title: there is no
    /// audio to line it up with, and holding it would leave a finished track on screen.
    /// </summary>
    [TestMethod]
    public void BlankMetadata_PublishesImmediatelyAndDropsAHeldTrack()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        gate.Submit(StreamMetadata.Empty);

        Assert.IsFalse(scheduler.HasPending);
        Assert.HasCount(2, published);
        Assert.AreEqual(string.Empty, published[1]);
        Assert.DoesNotContain("Held", published);
    }

    /// <summary>
    /// A station that blanks its title between tracks must not thereby turn the delay off for
    /// the rest of the session: only a real station start re-arms the immediate publish.
    /// </summary>
    [TestMethod]
    public void BlankMetadata_DoesNotReArmTheImmediatePublish()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));
        gate.Submit(StreamMetadata.Empty);

        gate.Submit(Track("Next Track"));

        Assert.DoesNotContain("Next Track", published);
        Assert.AreEqual(TimeSpan.FromSeconds(20), scheduler.ScheduledDelay);
    }

    /// <summary>
    /// A held track belongs to the stream it came from. After a station switch or a pause,
    /// publishing it would name a song the user is no longer listening to.
    /// </summary>
    [TestMethod]
    public void Reset_DropsAHeldTrackAndTreatsTheNextOneAsAStationStart()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        gate.Reset();
        gate.Submit(Track("New Station Track"));

        Assert.AreSequenceEqual(["Opening Track", "New Station Track"], published);
        Assert.IsFalse(scheduler.HasPending);
    }

    /// <summary>
    /// Everything that asks "what is playing" reads <see cref="MetadataPublishGate.Current"/>,
    /// including surfaces created part-way through a hold (a mini player opened mid-track) and
    /// the media transport controls, which refresh for unrelated reasons such as a station name
    /// change. If it leaked the held track, those surfaces would disagree with the audio.
    /// </summary>
    [TestMethod]
    public void Current_KeepsReportingThePreviousTrackForTheWholeHold()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, _) = NewGate(20);
        gate.Submit(Track("Opening Track"));

        gate.Submit(Track("Held"));

        Assert.AreEqual("Opening Track", gate.Current.DisplayText);

        scheduler.Fire();

        Assert.AreEqual("Held", gate.Current.DisplayText);
    }

    /// <summary>
    /// A thread-pool timer callback already on its way cannot be recalled, so a cancelled
    /// publication has to be recognised and dropped when it arrives.
    /// </summary>
    [TestMethod]
    public void AStaleTimerCallback_PublishesNothing()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        gate.Reset();
        scheduler.FireStale();

        Assert.AreSequenceEqual(["Opening Track"], published);
    }

    /// <summary>
    /// The same guard has to hold for the ordinary replacement case, where the timer for a
    /// superseded track fires just after a newer one has taken its place.
    /// </summary>
    [TestMethod]
    public void AStaleTimerCallbackAfterAReplacement_PublishesNothing()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Superseded"));

        // Grab the callback belonging to "Superseded" before "Actual" replaces it.
        Action? supersededTimer = scheduler.CaptureScheduled();
        gate.Submit(Track("Actual"));
        supersededTimer?.Invoke();

        Assert.AreSequenceEqual(["Opening Track"], published);
        Assert.AreEqual(TimeSpan.FromSeconds(20), scheduler.ScheduledDelay);
    }

    /// <summary>
    /// The popup's right-click menu changes the delay while a track is being held. Re-timing
    /// from the track's arrival makes the new value apply to the track in hand, which is the
    /// one the user is looking at when they reach for the menu.
    /// </summary>
    [TestMethod]
    public void ChangingTheDelayDuringAHold_ReTimesFromWhenTheTrackArrived()
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20, () => now);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        now = now.AddSeconds(8);
        gate.DelaySeconds = 15;

        Assert.AreEqual(TimeSpan.FromSeconds(7), scheduler.ScheduledDelay);
        Assert.DoesNotContain("Held", published);
    }

    [TestMethod]
    public void ShorteningTheDelayPastTheWaitAlreadyServed_PublishesTheHeldTrackAtOnce()
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20, () => now);
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        now = now.AddSeconds(8);
        gate.DelaySeconds = 5;

        Assert.AreSequenceEqual(["Opening Track", "Held"], published);
        Assert.IsFalse(scheduler.HasPending);
    }

    /// <summary>
    /// Pausing part-way through the wait means the track never becomes audible. Publishing it
    /// anyway would name a song over silence - and not just in the popup: the window, the mini
    /// player, the media controls and the playlist history would all pick it up.
    /// </summary>
    [TestMethod]
    public void AHeldTrack_IsDroppedWhenPlaybackStoppedDuringTheWait()
    {
        bool playing = true;
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.IsPlaybackActive = () => playing;
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        playing = false;
        scheduler.Fire();

        Assert.AreSequenceEqual(["Opening Track"], published);
        Assert.AreEqual("Opening Track", gate.Current.DisplayText);
    }

    /// <summary>
    /// Whatever plays next is a fresh start, so it should appear as it arrives rather than
    /// waiting out a delay meant for a stream that is no longer running.
    /// </summary>
    [TestMethod]
    public void AfterAHeldTrackIsDroppedForStoppedPlayback_TheNextTrackPublishesImmediately()
    {
        bool playing = true;
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.IsPlaybackActive = () => playing;
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        playing = false;
        scheduler.Fire();
        playing = true;
        gate.Submit(Track("After Resume"));

        Assert.AreSequenceEqual(["Opening Track", "After Resume"], published);
        Assert.IsFalse(scheduler.HasPending);
    }

    /// <summary>
    /// A stream stuttering mid-track is still playing that track, so the hold has to survive
    /// it - otherwise a rebuffer would mean the track was never shown at all.
    /// </summary>
    [TestMethod]
    public void AHeldTrack_SurvivesWhilePlaybackIsStillActive()
    {
        (MetadataPublishGate gate, FakeScheduler scheduler, List<string> published) = NewGate(20);
        gate.IsPlaybackActive = () => true;
        gate.Submit(Track("Opening Track"));
        gate.Submit(Track("Held"));

        scheduler.Fire();

        Assert.AreSequenceEqual(["Opening Track", "Held"], published);
    }

    /// <summary>
    /// Clearing the display is always allowed: that is what a stop looks like, and blocking it
    /// would strand a finished track on screen.
    /// </summary>
    [TestMethod]
    public void BlankMetadata_PublishesEvenWhenPlaybackHasStopped()
    {
        (MetadataPublishGate gate, _, List<string> published) = NewGate(20);
        gate.IsPlaybackActive = () => false;
        gate.Submit(Track("Opening Track"));

        gate.Submit(StreamMetadata.Empty);

        Assert.HasCount(2, published);
        Assert.AreEqual(string.Empty, published[1]);
    }

    [TestMethod]
    public void TheDelayIsClampedToTheSupportedRange()
    {
        (MetadataPublishGate gate, _, _) = NewGate(0);

        gate.DelaySeconds = 999;
        Assert.AreEqual(60, gate.DelaySeconds);

        gate.DelaySeconds = -5;
        Assert.AreEqual(0, gate.DelaySeconds);
    }
}
