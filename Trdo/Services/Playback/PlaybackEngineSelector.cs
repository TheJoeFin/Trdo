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
            return await PrepareNativeAsync(streamUrl, cancellationToken);
        }

        bool nativeFirst = mode == PlaybackEngineMode.NativePreferred;
        if (mode == PlaybackEngineMode.Auto &&
            TryGetPreferredBackend(streamUrl, out PlaybackBackendKind remembered))
        {
            Debug.WriteLine($"[PlaybackEngineSelector] Using remembered {remembered} preference for {streamUrl}");
            nativeFirst = remembered == PlaybackBackendKind.Native;
        }

        IPlaybackBackend first = nativeFirst ? _nativeBackend : _libVlcBackend;
        IPlaybackBackend second = nativeFirst ? _libVlcBackend : _nativeBackend;

        PlaybackPrepareResult firstResult = await PrepareWithBackendAsync(first, streamUrl, usedFallback: false, cancellationToken);
        if (firstResult.Success)
        {
            RememberPreferredBackend(streamUrl, first.Kind);
            return firstResult;
        }

        Debug.WriteLine($"[PlaybackEngineSelector] {first.Kind} prepare failed, falling back to {second.Kind}: {firstResult.ErrorMessage}");
        PlaybackPrepareResult fallbackResult = await PrepareWithBackendAsync(second, streamUrl, usedFallback: true, cancellationToken);
        if (fallbackResult.Success)
        {
            RememberPreferredBackend(streamUrl, second.Kind);
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

        Debug.WriteLine($"[PlaybackEngineSelector] Retrying with {other.Kind} fallback");
        PlaybackPrepareResult result = await PrepareWithBackendAsync(other, streamUrl, usedFallback: true, cancellationToken);
        if (result.Success)
        {
            RememberPreferredBackend(streamUrl, other.Kind);
        }

        return result;
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
