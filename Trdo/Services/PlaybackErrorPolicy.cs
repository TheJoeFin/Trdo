namespace Trdo.Services;

/// <summary>
/// What should happen to a reported playback error, judged against the state of
/// playback right now rather than at the moment the failure was raised.
/// </summary>
public enum PlaybackErrorVerdict
{
    /// <summary>Playback really is broken and there is somewhere to show the error.</summary>
    Show,

    /// <summary>
    /// Not yet: the outcome is still unfolding (a retry is buffering), or there is no
    /// visible surface to show a modal on. Ask again shortly.
    /// </summary>
    Hold,

    /// <summary>
    /// Never: the failure this error describes is over, or it was about a stream the
    /// user has already moved on from.
    /// </summary>
    Discard,
}

/// <summary>
/// Everything the <see cref="PlaybackErrorPolicy"/> needs to judge a reported error.
/// Gathered at the moment of the decision, not at the moment of the failure.
/// </summary>
public readonly record struct PlaybackErrorSignals
{
    /// <summary>Whether the active playback backend reports that it is playing.</summary>
    public bool IsPlaying { get; init; }

    /// <summary>Whether the player is buffering — i.e. an attempt is still in flight.</summary>
    public bool IsBuffering { get; init; }

    /// <summary>
    /// Whether there is a visible window with a presenter attached. A modal raised
    /// against a hidden window does not disappear — it waits there and ambushes the
    /// user the next time they open it.
    /// </summary>
    public bool IsHostVisible { get; init; }

    /// <summary>
    /// Whether the player has moved to a different stream since the error was reported.
    /// An error about the previous station is never worth showing.
    /// </summary>
    public bool StreamChangedSinceReport { get; init; }

    /// <summary>How long ago the failure was reported.</summary>
    public double AgeSeconds { get; init; }

    /// <summary>
    /// Whether the WASAPI loopback monitor is capturing. When it is not, there is no
    /// independent evidence about audio and the engine's own state is all we have.
    /// </summary>
    public bool IsAudioMonitorRunning { get; init; }

    /// <summary>
    /// How long ago the loopback last heard audio above the silence threshold, or
    /// <see langword="null"/> if it has never heard any.
    /// </summary>
    public double? SecondsSinceAudioHeard { get; init; }
}

/// <summary>
/// Decides whether a playback error still describes reality by the time it would reach
/// the screen. Kept free of WinRT/WinUI dependencies so it can be unit tested directly
/// (see Trdo.Tests).
/// <para>
/// A failure and its report are not simultaneous: the fallback engine may still be
/// starting, and stream diagnosis probes the network before the message is even built.
/// By the time a report lands the station is often playing perfectly well — and because
/// the tray popup keeps its page alive while hidden, a dialog raised then does not fail,
/// it queues up and greets the user when they next open the flyout. So the decision to
/// show has to be made against live state, and re-made until it is acted on.
/// </para>
/// </summary>
public static class PlaybackErrorPolicy
{
    /// <summary>
    /// How long an unshown error stays worth showing. An error is a reaction to something
    /// the user just did; once it has sat unseen this long it has become an interruption
    /// about ancient history, and the tray icon already conveys that nothing is playing.
    /// Pressing play again produces a fresh failure — and by then the flyout is open, so
    /// that one is shown immediately.
    /// </summary>
    public const double MaxAgeSeconds = 30;

    /// <summary>
    /// How recently the loopback must have heard audio to count as corroborating the
    /// engine. Long enough to ride out the gaps between tracks and quiet passages,
    /// short enough that a stream which has genuinely gone silent is not called healthy.
    /// </summary>
    public const double AudioFreshnessSeconds = 3;

    /// <summary>
    /// Whether playback is, right now, demonstrably fine — in which case an error saying
    /// otherwise is simply wrong and must not be shown.
    /// </summary>
    /// <remarks>
    /// The engine's own <see cref="PlaybackErrorSignals.IsPlaying"/> is the primary
    /// signal; the loopback exists only to contradict it, since an engine reporting
    /// "playing" while producing silence is the failure mode this whole error path
    /// was built for. Where the loopback has nothing to say — it is not running, or has
    /// not had time to hear anything yet — the engine is believed. Erring towards
    /// "healthy" is deliberate: a suppressed error costs the user a message they can
    /// recover by pressing play, while a spurious one costs them trust in every error
    /// the app shows.
    /// </remarks>
    public static bool IsPlaybackHealthy(in PlaybackErrorSignals signals)
    {
        if (!signals.IsPlaying)
            return false;

        if (!signals.IsAudioMonitorRunning)
            return true;

        if (signals.SecondsSinceAudioHeard is double secondsSinceAudio)
            return secondsSinceAudio <= AudioFreshnessSeconds;

        // Monitor running but nothing heard yet. Give it the same grace period before
        // treating silence as proof, so a report that lands the instant playback starts
        // is not shown against audio that has not had time to arrive.
        return signals.AgeSeconds <= AudioFreshnessSeconds;
    }

    /// <summary>
    /// Decides what to do with an error that has been reported but not yet shown.
    /// </summary>
    public static PlaybackErrorVerdict Evaluate(in PlaybackErrorSignals signals)
    {
        if (signals.StreamChangedSinceReport)
            return PlaybackErrorVerdict.Discard;

        if (IsPlaybackHealthy(signals))
            return PlaybackErrorVerdict.Discard;

        if (signals.AgeSeconds > MaxAgeSeconds)
            return PlaybackErrorVerdict.Discard;

        // A retry is still loading. Whether this failure mattered is not yet decided,
        // and if the retry fails the failure is reported again.
        if (signals.IsBuffering)
            return PlaybackErrorVerdict.Hold;

        if (!signals.IsHostVisible)
            return PlaybackErrorVerdict.Hold;

        return PlaybackErrorVerdict.Show;
    }

    /// <summary>
    /// Decides whether an error already on screen should be taken back down.
    /// </summary>
    /// <remarks>
    /// Age is deliberately not a reason to withdraw: once the user is looking at a
    /// dialog it is theirs to dismiss. Only the error becoming untrue — playback
    /// recovered, or the user switched station — takes it away from them.
    /// </remarks>
    public static bool ShouldWithdraw(in PlaybackErrorSignals signals)
    {
        return signals.StreamChangedSinceReport || IsPlaybackHealthy(signals);
    }
}
