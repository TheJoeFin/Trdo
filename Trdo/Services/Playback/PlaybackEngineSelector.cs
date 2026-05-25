using System;
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
    private const string BackendPreferencePrefix = "PlaybackBackendPref_";

    private readonly NativePlaybackBackend _nativeBackend;
    private readonly LibVlcPlaybackBackend? _libVlcBackend;
    private IPlaybackBackend _activeBackend;
    private bool _nativeFailedForCurrentUrl;

    public PlaybackEngineSelector(NativePlaybackBackend nativeBackend, LibVlcPlaybackBackend? libVlcBackend)
    {
        _nativeBackend = nativeBackend;
        _libVlcBackend = libVlcBackend;
        _activeBackend = nativeBackend;
    }

    public IPlaybackBackend ActiveBackend => _activeBackend;

    public PlaybackBackendKind ActiveBackendKind => _activeBackend.Kind;

    public event EventHandler<PlaybackBackendKind>? ActiveBackendChanged;

    public static PlaybackEngineMode GetEngineMode() => SettingsService.PlaybackEngineMode;

    public static void SetEngineMode(PlaybackEngineMode mode) => SettingsService.PlaybackEngineMode = mode;

    public async Task<PlaybackPrepareResult> PrepareAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        _nativeFailedForCurrentUrl = false;
        PlaybackEngineMode mode = GetEngineMode();

        if (mode == PlaybackEngineMode.LibVlcPreferred)
        {
            return await PrepareWithBackendAsync(_libVlcBackend, streamUrl, usedFallback: false, cancellationToken);
        }

        if (TryGetPreferredBackend(streamUrl, out PlaybackBackendKind preferred) &&
            preferred == PlaybackBackendKind.LibVlc &&
            _libVlcBackend is not null &&
            mode == PlaybackEngineMode.Auto)
        {
            Debug.WriteLine($"[PlaybackEngineSelector] Using remembered LibVLC preference for {streamUrl}");
            return await PrepareWithBackendAsync(_libVlcBackend, streamUrl, usedFallback: true, cancellationToken);
        }

        if (mode == PlaybackEngineMode.NativeOnly || _libVlcBackend is null)
        {
            return await PrepareNativeAsync(streamUrl, cancellationToken);
        }

        PlaybackPrepareResult nativeResult = await PrepareNativeAsync(streamUrl, cancellationToken);
        if (nativeResult.Success)
        {
            RememberPreferredBackend(streamUrl, PlaybackBackendKind.Native);
            return nativeResult;
        }

        Debug.WriteLine($"[PlaybackEngineSelector] Native prepare failed, falling back to LibVLC: {nativeResult.ErrorMessage}");
        _nativeFailedForCurrentUrl = true;
        PlaybackPrepareResult fallbackResult = await PrepareWithBackendAsync(_libVlcBackend, streamUrl, usedFallback: true, cancellationToken);
        if (fallbackResult.Success)
        {
            RememberPreferredBackend(streamUrl, PlaybackBackendKind.LibVlc);
        }

        return fallbackResult;
    }

    public async Task<PlaybackPrepareResult> RetryWithFallbackAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        if (_libVlcBackend is null || _activeBackend.Kind == PlaybackBackendKind.LibVlc)
        {
            return await PrepareAsync(streamUrl, cancellationToken);
        }

        if (!_nativeFailedForCurrentUrl)
        {
            _nativeFailedForCurrentUrl = true;
        }

        Debug.WriteLine("[PlaybackEngineSelector] Retrying with LibVLC fallback");
        PlaybackPrepareResult result = await PrepareWithBackendAsync(_libVlcBackend, streamUrl, usedFallback: true, cancellationToken);
        if (result.Success)
        {
            RememberPreferredBackend(streamUrl, PlaybackBackendKind.LibVlc);
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
