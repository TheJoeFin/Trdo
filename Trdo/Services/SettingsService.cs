using Windows.Storage;

namespace Trdo.Services;

/// <summary>
/// Centralized service for managing application settings
/// </summary>
public static class SettingsService
{
    private const string IsFirstRunKey = "IsFirstRun";
    private const string IsVolumeSliderVisibleKey = "IsVolumeSliderVisible";
    private const string AutoPlayOnStartupKey = "AutoPlayOnStartup";
    private const string IsSpotifyEnabledKey = "IsSpotifyEnabled";
    private const string IsDiscogsEnabledKey = "IsDiscogsEnabled";
    private const string IsAppleMusicEnabledKey = "IsAppleMusicEnabled";
    private const string IsYouTubeMusicEnabledKey = "IsYouTubeMusicEnabled";

    /// <summary>
    /// Gets or sets whether the app should automatically start playing the last selected station on startup.
    /// Defaults to false when no saved value exists.
    /// </summary>
    public static bool AutoPlayOnStartup
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(AutoPlayOnStartupKey, out object? value))
                {
                    return value switch
                    {
                        bool b => b,
                        string s when bool.TryParse(s, out bool b2) => b2,
                        _ => false
                    };
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[AutoPlayOnStartupKey] = value;
            }
            catch
            {
                // Silently fail if unable to save
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the volume slider is visible on the playing page.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsVolumeSliderVisible
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsVolumeSliderVisibleKey, out object? value))
                {
                    return value switch
                    {
                        bool b => b,
                        string s when bool.TryParse(s, out bool b2) => b2,
                        _ => true
                    };
                }
                return true;
            }
            catch
            {
                return true;
            }
        }
        set
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[IsVolumeSliderVisibleKey] = value;
            }
            catch
            {
                // Silently fail if unable to save
            }
        }
    }

    /// <summary>
    /// Gets or sets whether Spotify search links are shown.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsSpotifyEnabled
    {
        get => GetBoolSetting(IsSpotifyEnabledKey, defaultValue: true);
        set => SetBoolSetting(IsSpotifyEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets whether Discogs search links are shown.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsDiscogsEnabled
    {
        get => GetBoolSetting(IsDiscogsEnabledKey, defaultValue: true);
        set => SetBoolSetting(IsDiscogsEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets whether Apple Music search links are shown.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsAppleMusicEnabled
    {
        get => GetBoolSetting(IsAppleMusicEnabledKey, defaultValue: true);
        set => SetBoolSetting(IsAppleMusicEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets whether YouTube Music search links are shown.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsYouTubeMusicEnabled
    {
        get => GetBoolSetting(IsYouTubeMusicEnabledKey, defaultValue: true);
        set => SetBoolSetting(IsYouTubeMusicEnabledKey, value);
    }

    private static bool GetBoolSetting(string key, bool defaultValue)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value))
            {
                return value switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out bool b2) => b2,
                    _ => defaultValue
                };
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void SetBoolSetting(string key, bool value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch
        {
            // Silently fail if unable to save
        }
    }

    /// <summary>
    /// Gets whether this is the first run of the application
    /// </summary>
    public static bool IsFirstRun
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsFirstRunKey, out object? value))
                {
                    return value switch
                    {
                        bool b => b,
                        string s when bool.TryParse(s, out bool b2) => b2,
                        _ => true // Default to true if value is unexpected
                    };
                }
                // If key doesn't exist, it's the first run
                return true;
            }
            catch
            {
                // If any error occurs, default to true
                return true;
            }
        }
    }

    /// <summary>
    /// Marks that the first run has been completed
    /// </summary>
    public static void MarkFirstRunComplete()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[IsFirstRunKey] = false;
        }
        catch
        {
            // Silently fail if unable to save
        }
    }
}
