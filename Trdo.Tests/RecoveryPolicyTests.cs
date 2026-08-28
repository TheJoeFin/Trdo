using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trdo.Services.Playback;

namespace Trdo.Tests;

/// <summary>
/// Covers the escalation ladder that decides how far stream recovery goes.
/// The scenarios mirror the four cases from the original issue report: a stable station,
/// an intermittent one, a severely broken one, and the requirement that an in-app reset
/// produces the same clean slate as restarting the app.
/// </summary>
[TestClass]
public sealed class RecoveryPolicyTests
{
    private DateTime _now;

    private RecoveryPolicy CreatePolicy(TimeSpan? failureWindow = null, TimeSpan? stabilityInterval = null)
    {
        _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new RecoveryPolicy(() => _now, failureWindow, stabilityInterval);
    }

    private void Advance(TimeSpan amount) => _now += amount;

    /// <summary>Holds playback steady for the given span, polling as the watchdog does.</summary>
    private void PlayFor(RecoveryPolicy policy, TimeSpan duration)
    {
        TimeSpan poll = TimeSpan.FromSeconds(5);
        policy.RecordPlaybackConfirmed();

        for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += poll)
        {
            Advance(poll);
            policy.RecordPlaybackConfirmed();
        }
    }

    // --- Case 1: stable station -------------------------------------------------------

    [TestMethod]
    public void StableStation_NeverEscalatesAndNeverBuffersUp()
    {
        RecoveryPolicy policy = CreatePolicy();

        PlayFor(policy, TimeSpan.FromMinutes(10));

        Assert.AreEqual(0, policy.AutoBufferBump, "A healthy stream must not inflate the buffer.");
        Assert.AreEqual(0, policy.FailuresInWindow);
        Assert.IsFalse(policy.HasGivenUp);
        Assert.AreEqual(RecoveryAction.SoftRetry, policy.CurrentAction, "Ladder should still be at its lowest rung.");
    }

    [TestMethod]
    public void FirstFailureOnAHealthyStream_IsASoftRetry()
    {
        RecoveryPolicy policy = CreatePolicy();
        PlayFor(policy, TimeSpan.FromMinutes(5));

        Assert.AreEqual(RecoveryAction.SoftRetry, policy.RecordFailure());
        Assert.AreEqual(0, policy.AutoBufferBump, "A soft retry alone should not raise the buffer.");
    }

    // --- Case 2: intermittent station -------------------------------------------------

    [TestMethod]
    public void IntermittentStation_EscalatesGraduallyAndBumpsBufferOnce()
    {
        RecoveryPolicy policy = CreatePolicy();

        Assert.AreEqual(RecoveryAction.SoftRetry, policy.RecordFailure());
        Assert.AreEqual(0, policy.AutoBufferBump);

        Advance(TimeSpan.FromSeconds(20));
        Assert.AreEqual(RecoveryAction.RebuildPipeline, policy.RecordFailure());
        Assert.AreEqual(1, policy.AutoBufferBump, "Rebuilding should ask for one more buffer level.");

        Assert.IsFalse(policy.HasGivenUp, "Two failures is nowhere near giving up.");
    }

    [TestMethod]
    public void SustainedPlayback_StepsTheLadderDownOneRungPerStabilityInterval()
    {
        TimeSpan stability = TimeSpan.FromSeconds(60);
        RecoveryPolicy policy = CreatePolicy(stabilityInterval: stability);

        policy.RecordFailure();                 // SoftRetry
        Advance(TimeSpan.FromSeconds(10));
        policy.RecordFailure();                 // RebuildPipeline, bump -> 1
        Assert.AreEqual(1, policy.AutoBufferBump);
        Assert.AreEqual(RecoveryAction.SwitchBackend, policy.CurrentAction);

        // De-escalation is deliberately gradual: recovering fast would let a stream that
        // only looks healthy between drop-outs keep resetting the ladder.
        PlayFor(policy, stability + TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, policy.AutoBufferBump, "The bump must decay once the stream holds up.");
        Assert.AreEqual(
            RecoveryAction.RebuildPipeline,
            policy.CurrentAction,
            "One stability interval should step the ladder down exactly one rung.");

        PlayFor(policy, stability * 2);

        Assert.AreEqual(
            RecoveryAction.SoftRetry,
            policy.CurrentAction,
            "Continued healthy playback should return the ladder to its gentlest rung.");
        Assert.AreEqual(0, policy.FailuresInWindow, "A fully recovered stream should have a clean window.");
    }

    [TestMethod]
    public void ASingleHealthyPoll_DoesNotDeEscalate()
    {
        // This is the regression that let a flapping stream sit at "attempt 1/3" forever:
        // the old counter reset on any healthy observation, so it never escalated.
        RecoveryPolicy policy = CreatePolicy(stabilityInterval: TimeSpan.FromSeconds(60));

        policy.RecordFailure();                 // SoftRetry
        Advance(TimeSpan.FromSeconds(5));
        policy.RecordPlaybackConfirmed();       // one brief healthy poll
        Advance(TimeSpan.FromSeconds(5));

        Assert.AreEqual(
            RecoveryAction.RebuildPipeline,
            policy.RecordFailure(),
            "A brief flicker of health must not rewind the ladder.");
    }

    [TestMethod]
    public void FailuresOlderThanTheWindow_StopCountingTowardStutter()
    {
        TimeSpan window = TimeSpan.FromMinutes(2);
        RecoveryPolicy policy = CreatePolicy(failureWindow: window);

        policy.RecordFailure();
        Assert.AreEqual(1, policy.FailuresInWindow);

        Advance(window + TimeSpan.FromSeconds(30));
        policy.RecordFailure();

        Assert.AreEqual(1, policy.FailuresInWindow, "The stale failure should have aged out of the window.");
    }

    // --- Case 3: severely broken station ----------------------------------------------

    [TestMethod]
    public void BrokenStation_WalksTheFullLadderInOrder()
    {
        RecoveryPolicy policy = CreatePolicy();

        Assert.AreEqual(RecoveryAction.SoftRetry, policy.RecordFailure());
        Assert.AreEqual(RecoveryAction.RebuildPipeline, policy.RecordFailure());
        Assert.AreEqual(RecoveryAction.SwitchBackend, policy.RecordFailure());
        Assert.AreEqual(RecoveryAction.BackOff, policy.RecordFailure());
    }

    [TestMethod]
    public void BrokenStation_GivesUpAfterTheBackOffCeiling()
    {
        RecoveryPolicy policy = CreatePolicy();

        RecoveryAction action = RecoveryAction.SoftRetry;
        for (int i = 0; i < 20 && action != RecoveryAction.GiveUp; i++)
        {
            Advance(TimeSpan.FromSeconds(5));
            action = policy.RecordFailure();
        }

        Assert.AreEqual(RecoveryAction.GiveUp, action, "A hopeless stream must eventually stop being retried.");
        Assert.IsTrue(policy.HasGivenUp);
    }

    [TestMethod]
    public void BufferBump_StopsAtTheCeiling()
    {
        RecoveryPolicy policy = CreatePolicy();

        for (int i = 0; i < 30; i++)
        {
            Advance(TimeSpan.FromSeconds(5));
            policy.RecordFailure();
        }

        Assert.IsLessThanOrEqualTo(
            RecoveryPolicy.MaxAutoBufferBump, policy.AutoBufferBump,
            $"Buffer bump ran away to {policy.AutoBufferBump}.");
    }

    [TestMethod]
    public void BackOff_ReEntersAtTheBackendSwitchRungNotTheBottom()
    {
        RecoveryPolicy policy = CreatePolicy();

        policy.RecordFailure();                                     // SoftRetry
        policy.RecordFailure();                                     // RebuildPipeline
        policy.RecordFailure();                                     // SwitchBackend
        Assert.AreEqual(RecoveryAction.BackOff, policy.RecordFailure());

        Assert.AreEqual(
            RecoveryAction.SwitchBackend,
            policy.RecordFailure(),
            "After backing off we should retry the backend switch, not restart at a soft retry.");
    }

    [TestMethod]
    public void AutoBufferDisabled_StillEscalatesButNeverRaisesTheBuffer()
    {
        RecoveryPolicy policy = CreatePolicy();
        policy.AutoBufferIncreaseEnabled = false;

        Assert.AreEqual(RecoveryAction.SoftRetry, policy.RecordFailure());
        Assert.AreEqual(RecoveryAction.RebuildPipeline, policy.RecordFailure());
        Assert.AreEqual(RecoveryAction.SwitchBackend, policy.RecordFailure());

        Assert.AreEqual(0, policy.AutoBufferBump, "The user opted out of automatic buffering.");
    }

    // --- Case 4: in-app reset == app restart ------------------------------------------

    [TestMethod]
    public void ResetForStation_ProducesTheSameStateAsAFreshInstance()
    {
        RecoveryPolicy policy = CreatePolicy();

        for (int i = 0; i < 6; i++)
        {
            Advance(TimeSpan.FromSeconds(5));
            policy.RecordFailure();
        }

        Assert.IsGreaterThan(0, policy.FailuresInWindow);
        Assert.IsGreaterThan(0, policy.AutoBufferBump);

        policy.ResetForStation();

        RecoveryPolicy fresh = new(() => _now);
        Assert.AreEqual(fresh.FailuresInWindow, policy.FailuresInWindow);
        Assert.AreEqual(fresh.AutoBufferBump, policy.AutoBufferBump);
        Assert.AreEqual(fresh.CurrentAction, policy.CurrentAction);
        Assert.AreEqual(fresh.HasGivenUp, policy.HasGivenUp);
    }

    [TestMethod]
    public void ResetForStation_ClearsAGiveUpSoTheUserCanRetry()
    {
        RecoveryPolicy policy = CreatePolicy();

        RecoveryAction action = RecoveryAction.SoftRetry;
        for (int i = 0; i < 20 && action != RecoveryAction.GiveUp; i++)
        {
            Advance(TimeSpan.FromSeconds(5));
            action = policy.RecordFailure();
        }
        Assert.IsTrue(policy.HasGivenUp);

        policy.ResetForStation();

        Assert.IsFalse(policy.HasGivenUp);
        Assert.AreEqual(
            RecoveryAction.SoftRetry,
            policy.RecordFailure(),
            "A fresh station or an explicit retry deserves the gentlest rung again.");
    }
}
