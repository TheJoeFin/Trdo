using System;
using System.Diagnostics;
using LibVLCSharp.Shared;

namespace Trdo.Services.Playback;

/// <summary>
/// Manages the shared LibVLC native instance for fallback playback.
/// </summary>
public static class LibVlcHost
{
    private static LibVLC? _instance;
    private static bool _initializeAttempted;

    public static LibVLC? Instance => _instance;

    public static bool IsAvailable => _instance is not null;

    public static bool TryInitialize()
    {
        if (_initializeAttempted)
        {
            return _instance is not null;
        }

        _initializeAttempted = true;

        try
        {
            Core.Initialize();
            _instance = new LibVLC("--no-video");
            Debug.WriteLine("[LibVlcHost] LibVLC initialized");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LibVlcHost] Failed to initialize LibVLC: {ex.Message}");
            _instance = null;
            return false;
        }
    }

    public static void Dispose()
    {
        _instance?.Dispose();
        _instance = null;
        _initializeAttempted = false;
    }
}
