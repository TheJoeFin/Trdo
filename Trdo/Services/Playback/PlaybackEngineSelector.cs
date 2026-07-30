using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Services;
using Trdo.Services.Playback;
using Windows.Storage;

namespace Trdo.Services.Playback;

public sealed class PlaybackEngineSelector : IDisposable
{
    // Pre-2.0 preferences meant "native worked here"; under the LibVLC-first
    // default they would wrongly force native, so 2.0 uses a new prefix and
    // deletes keys stored under the old one.
    private const string LegacyBackendPreferencePrefix = "PlaybackBackendPref_";
    private const string BackendPreferencePrefix = "PlaybackBackendPref2_";

    private readonly NativePlaybackBackend _nativeBackend;
    private readonly LibVlcPlaybackBackend? _libVlcBackend;
    private IPlaybackBackend _activeBackend;

    public PlaybackEngineSelector(NativePlaybackBackend nativeBackend, LibVlcPlaybackBackend? libVlcBackend)
    {
        _nativeBackend = nativeBackend;
        _libVlcBackend = libVlcBackend;
        _activeBackend = nativeBackend;
        CleanupLegacyPreferences();
    }

    public IPlaybackBackend ActiveBackend => _activeBackend;

    public PlaybackBackendKind ActiveBackendKind => _activeBackend.Kind;

    public event EventHandler<PlaybackBackendKind>? ActiveBackendChanged;

    public static PlaybackEngineMode GetEngineMode() => SettingsService.PlaybackEngineMode;

    public static void SetEngineMode(PlaybackEngineMode mode) => SettingsService.PlaybackEngineMode = mode;

    public async Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        PlaybackEngineMode mode = GetEngineMode();

        if (mode == PlaybackEngineMode.NativeOnly || _libVlcBackend is null)
        {
            LogService.Info("PlaybackEngineSelector",
                $"Mode={mode}, LibVLC available={_libVlcBackend is not null} -> Native only for {LogService.Redact(streamUrl)}");
            return await PrepareNativeAsync(streamUrl, cancellationToken);
        }

        bool nativeFirst = mode == PlaybackEngineMode.NativePreferred;
        bool usedRemembered = false;
        if (mode == PlaybackEngineMode.Auto &&
            TryGetPreferredBackend(streamUrl, out PlaybackBackendKind remembered))
        {
            Debug.WriteLine($"[PlaybackEngineSelector] Using remembered {remembered} preference for {streamUrl}");
            nativeFirst = remembered == PlaybackBackendKind.Native;
            usedRemembered = true;
        }

        IPlaybackBackend first = nativeFirst ? _nativeBackend : _libVlcBackend;
        IPlaybackBackend second = nativeFirst ? _libVlcBackend : _nativeBackend;

        LogService.Info("PlaybackEngineSelector",
            $"Mode={mode}, remembered={usedRemembered}, trying {first.Kind} first for {LogService.Redact(streamUrl)}");

        // Note: preparing successfully is not evidence a backend can play this stream -
        // LibVlcPlaybackBackend.PrepareAsync always succeeds. The preference is only
        // written once playback is actually confirmed, via ConfirmBackendHealthy.
        PlaybackPrepareResult firstResult = await PrepareWithBackendAsync(first, streamUrl, usedFallback: false, cancellationToken);
        if (firstResult.Success)
        {
            LogService.Info("PlaybackEngineSelector", $"{first.Kind} prepared successfully");
            return firstResult;
        }

        LogService.Warn("PlaybackEngineSelector",
            $"{first.Kind} prepare failed ({firstResult.ErrorMessage}); falling back to {second.Kind}");
        Debug.WriteLine($"[PlaybackEngineSelector] {first.Kind} prepare failed, falling back to {second.Kind}: {firstResult.ErrorMessage}");
        PlaybackPrepareResult fallbackResult = await PrepareWithBackendAsync(second, streamUrl, usedFallback: true, cancellationToken);
        if (fallbackResult.Success)
        {
            LogService.Warn("PlaybackEngineSelector", $"Fallback to {second.Kind} succeeded");
        }
        else
        {
            LogService.Error("PlaybackEngineSelector",
                $"Both backends failed to prepare; last error: {fallbackResult.ErrorMessage}");
        }

        return fallbackResult;
    }

    public async Task<PlaybackPrepareResult> RetryWithFallbackAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        IPlaybackBackend? other = _activeBackend.Kind == PlaybackBackendKind.LibVlc
            ? _nativeBackend
            : _libVlcBackend;

        if (other is null || GetEngineMode() == PlaybackEngineMode.NativeOnly)
        {
            return await PrepareAsync(streamUrl, cancellationToken);
        }

        LogService.Warn("PlaybackEngineSelector", $"Retrying with {other.Kind} fallback for {LogService.Redact(streamUrl)}");
        Debug.WriteLine($"[PlaybackEngineSelector] Retrying with {other.Kind} fallback");
        PlaybackPrepareResult result = await PrepareWithBackendAsync(other, streamUrl, usedFallback: true, cancellationToken);
        if (result.Success)
        {
            LogService.Info("PlaybackEngineSelector", $"{other.Kind} fallback retry succeeded");
        }
        else
        {
            LogService.Error("PlaybackEngineSelector", $"{other.Kind} fallback retry failed: {result.ErrorMessage}");
        }

        return result;
    }

    /// <summary>
    /// Records that a backend has actually played this stream, so it is tried first next time.
    /// Call this only on a confirmed playing transition - prepare success is not sufficient
    /// evidence, since the LibVLC backend's prepare cannot fail.
    /// </summary>
    public void ConfirmBackendHealthy(string streamUrl, PlaybackBackendKind backend)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return;
        }

        if (TryGetPreferredBackend(streamUrl, out PlaybackBackendKind existing) && existing == backend)
        {
            return;
        }

        RememberPreferredBackend(streamUrl, backend);
        LogService.Info("PlaybackEngineSelector",
            $"Confirmed {backend} plays {LogService.Redact(streamUrl)}; remembering it");
    }

    /// <summary>
    /// Records that a backend repeatedly failed to play this stream, so the next prepare
    /// starts with the other one. Without this, a station that LibVLC can open but not play
    /// stays pinned to LibVLC forever.
    /// </summary>
    public void MarkBackendUnhealthy(string streamUrl, PlaybackBackendKind backend)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return;
        }

        PlaybackBackendKind other = backend == PlaybackBackendKind.LibVlc
            ? PlaybackBackendKind.Native
            : PlaybackBackendKind.LibVlc;

        // Point the preference at the other backend rather than just clearing it, so the
        // next attempt actively avoids the one that just failed instead of falling back
        // to the mode default (which may be the failing backend again).
        RememberPreferredBackend(streamUrl, other);
        LogService.Warn("PlaybackEngineSelector",
            $"{backend} marked unhealthy for {LogService.Redact(streamUrl)}; preferring {other} next");
    }

    public async Task<bool> WaitForNativeOpenAsync(CancellationToken cancellationToken, TimeSpan timeout)
    {
        if (_activeBackend.Kind != PlaybackBackendKind.Native)
        {
            return true;
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_nativeBackend.IsPlaying)
            {
                return true;
            }

            if (_nativeBackend.GetBufferedRanges().Count > 0 || _nativeBackend.BufferingProgress > 0)
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private async Task<PlaybackPrepareResult> PrepareNativeAsync(string streamUrl, CancellationToken cancellationToken)
    {
        SetActiveBackend(_nativeBackend);
        return await _nativeBackend.PrepareAsync(streamUrl, cancellationToken);
    }

    private async Task<PlaybackPrepareResult> PrepareWithBackendAsync(
        IPlaybackBackend? backend,
        string streamUrl,
        bool usedFallback,
        CancellationToken cancellationToken)
    {
        if (backend is null)
        {
            return PlaybackPrepareResult.Failed(PlaybackBackendKind.Native, "LibVLC backend is not available");
        }

        SetActiveBackend(backend);
        PlaybackPrepareResult result = await backend.PrepareAsync(streamUrl, cancellationToken);
        if (result.Success && usedFallback)
        {
            return PlaybackPrepareResult.Succeeded(backend.Kind, usedFallback: true);
        }

        return result;
    }

    private void SetActiveBackend(IPlaybackBackend backend)
    {
        if (ReferenceEquals(_activeBackend, backend))
        {
            return;
        }

        if (_activeBackend.Kind != backend.Kind)
        {
            _activeBackend.ClearSource();
        }

        _activeBackend = backend;
        ActiveBackendChanged?.Invoke(this, backend.Kind);
    }

    private static string GetPreferenceKey(string streamUrl)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(streamUrl));
        return BackendPreferencePrefix + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static bool TryGetPreferredBackend(string streamUrl, out PlaybackBackendKind backend)
    {
        backend = PlaybackBackendKind.Native;
        try
        {
            string key = GetPreferenceKey(streamUrl);
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value))
            {
                if (value is int i && Enum.IsDefined(typeof(PlaybackBackendKind), i))
                {
                    backend = (PlaybackBackendKind)i;
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void CleanupLegacyPreferences()
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            List<string> legacyKeys = [];
            foreach (string key in settings.Keys)
            {
                if (key.StartsWith(LegacyBackendPreferencePrefix, StringComparison.Ordinal))
                {
                    legacyKeys.Add(key);
                }
            }

            foreach (string key in legacyKeys)
            {
                settings.Remove(key);
            }

            if (legacyKeys.Count > 0)
            {
                Debug.WriteLine($"[PlaybackEngineSelector] Removed {legacyKeys.Count} legacy backend preference(s)");
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void RememberPreferredBackend(string streamUrl, PlaybackBackendKind backend)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[GetPreferenceKey(streamUrl)] = (int)backend;
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        _nativeBackend.Dispose();
        _libVlcBackend?.Dispose();
        _activeBackend = _nativeBackend;
    }
}
