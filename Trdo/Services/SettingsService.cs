using System;
using Trdo.Services.Playback;
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
    private const string TrayClickBehaviorKey = "TrayClickBehavior";
    private const string PlaybackEngineModeKey = "PlaybackEngineMode";
    private const string IsMiniPlayerVisualizerEnabledKey = "IsMiniPlayerVisualizerEnabled";
    private const string IsMiniPlayerTopmostKey = "IsMiniPlayerTopmost";
    private const string AllowSleepWhilePlayingKey = "AllowSleepWhilePlaying";
    private const string IsSongChangePopupEnabledKey = "IsSongChangePopupEnabled";
    private const string SongChangePopupDelaySecondsKey = "SongChangePopupDelaySeconds";
    private const string StationSortModeKey = "StationSortMode";

    public static event EventHandler? MusicSearchServicesChanged;

    /// <summary>
    /// Raised when <see cref="IsSongChangePopupEnabled"/> changes, so a popup
    /// that is currently on screen can be dismissed the moment the user turns
    /// the feature off.
    /// </summary>
    public static event EventHandler? SongChangePopupEnabledChanged;

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
        get => GetBoolSetting(IsAppleMusicEnabledKey, defaultValue: false);
        set => SetBoolSetting(IsAppleMusicEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets whether YouTube Music search links are shown.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsYouTubeMusicEnabled
    {
        get => GetBoolSetting(IsYouTubeMusicEnabledKey, defaultValue: false);
        set => SetBoolSetting(IsYouTubeMusicEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets the preferred playback engine mode.
    /// 0 = Auto (LibVLC first), 1 = Native only, 3 = Native preferred.
    /// The legacy value 2 (LibVLC preferred) reads as Auto, which now has
    /// the same behavior plus a native fallback.
    /// </summary>
    public static PlaybackEngineMode PlaybackEngineMode
    {
        get
        {
            try
            {
                PlaybackEngineMode mode = PlaybackEngineMode.Auto;
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(PlaybackEngineModeKey, out object? value))
                {
                    mode = value switch
                    {
                        int i when Enum.IsDefined(typeof(PlaybackEngineMode), i) => (PlaybackEngineMode)i,
                        string s when int.TryParse(s, out int parsed) && Enum.IsDefined(typeof(PlaybackEngineMode), parsed)
                            => (PlaybackEngineMode)parsed,
                        _ => PlaybackEngineMode.Auto
                    };
                }

                return mode == PlaybackEngineMode.LibVlcPreferred ? PlaybackEngineMode.Auto : mode;
            }
            catch
            {
                return PlaybackEngineMode.Auto;
            }
        }
        set
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[PlaybackEngineModeKey] = (int)value;
            }
            catch
            {
                // Silently fail if unable to save
            }
        }
    }

    /// <summary>
    /// Raised when <see cref="StationSortMode"/> changes, so the station list can re-render
    /// and switch dragging on or off.
    /// </summary>
    public static event EventHandler? StationSortModeChanged;

    /// <summary>
    /// Gets or sets how the station list is ordered on screen.
    /// <para>
    /// A view setting, not a data one: anything other than
    /// <see cref="Models.StationSortMode.Manual"/> changes what is drawn and leaves the saved
    /// order, folders and dividers untouched.
    /// </para>
    /// </summary>
    public static Models.StationSortMode StationSortMode
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(StationSortModeKey, out object? value))
                {
                    return value switch
                    {
                        int i when Enum.IsDefined(typeof(Models.StationSortMode), i) => (Models.StationSortMode)i,
                        string s when int.TryParse(s, out int parsed) && Enum.IsDefined(typeof(Models.StationSortMode), parsed)
                            => (Models.StationSortMode)parsed,
                        _ => Models.StationSortMode.Manual
                    };
                }
            }
            catch
            {
                // Fall through to the default
            }

            return Models.StationSortMode.Manual;
        }
        set
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[StationSortModeKey] = (int)value;
            }
            catch
            {
                // Silently fail if unable to save
            }

            StationSortModeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets or sets whether the spectrum visualizer is shown in the mini player.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsMiniPlayerVisualizerEnabled
    {
        get => GetBoolSetting(IsMiniPlayerVisualizerEnabledKey, defaultValue: true);
        set => SetBoolSetting(IsMiniPlayerVisualizerEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets whether the mini player window stays on top of other windows.
    /// Defaults to true when no saved value exists.
    /// </summary>
    public static bool IsMiniPlayerTopmost
    {
        get => GetBoolSetting(IsMiniPlayerTopmostKey, defaultValue: true);
        set => SetBoolSetting(IsMiniPlayerTopmostKey, value);
    }

    /// <summary>
    /// Gets or sets the tray icon click behavior.
    /// 0 = left click plays/pauses, right click opens flyout (default).
    /// 1 = left click opens flyout, right click plays/pauses.
    /// </summary>
    public static int TrayClickBehavior
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(TrayClickBehaviorKey, out object? value))
                {
                    return value switch
                    {
                        int i => i,
                        string s when int.TryParse(s, out int i2) => i2,
                        _ => 0
                    };
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        set
        {
            try
            {
                // Only valid values are 0 (default) and 1 (swapped)
                if (value < 0 || value > 1)
                    value = 0;
                ApplicationData.Current.LocalSettings.Values[TrayClickBehaviorKey] = value;
            }
            catch
            {
                // Silently fail if unable to save
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the PC may go to sleep while radio is playing.
    /// Defaults to false (keep the PC awake), matching pre-2.0 behavior.
    /// </summary>
    public static bool AllowSleepWhilePlaying
    {
        get => GetBoolSetting(AllowSleepWhilePlayingKey, defaultValue: false);
        set => SetBoolSetting(AllowSleepWhilePlayingKey, value);
    }

    /// <summary>
    /// Gets or sets whether a brief on-screen popup appears near the taskbar
    /// whenever the playing song changes. Opt-in; defaults to false so
    /// existing users see no new UI until they enable it.
    /// </summary>
    public static bool IsSongChangePopupEnabled
    {
        get => GetBoolSetting(IsSongChangePopupEnabledKey, defaultValue: false);
        set => SetBoolSetting(IsSongChangePopupEnabledKey, value);
    }

    /// <summary>
    /// Gets or sets how long to wait after a song change before showing the popup, in
    /// seconds. Defaults to no delay. Stations whose metadata runs ahead of the audio can
    /// override this individually via <see cref="Models.RadioStation.SongPopupDelaySeconds"/>.
    /// </summary>
    public static double SongChangePopupDelaySeconds
    {
        get => SongChangeAnnouncementPolicy.ClampDelay(
            GetDoubleSetting(SongChangePopupDelaySecondsKey, defaultValue: 0));
        set => SetDoubleSetting(
            SongChangePopupDelaySecondsKey,
            SongChangeAnnouncementPolicy.ClampDelay(value));
    }

    private static double GetDoubleSetting(string key, double defaultValue)
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value))
            {
                return value switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    string s when double.TryParse(
                        s,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double parsed) => parsed,
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

    private static void SetDoubleSetting(string key, double value)
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
            if (key is IsSpotifyEnabledKey or IsDiscogsEnabledKey or IsAppleMusicEnabledKey or IsYouTubeMusicEnabledKey)
            {
                MusicSearchServicesChanged?.Invoke(null, EventArgs.Empty);
            }
            else if (key is IsSongChangePopupEnabledKey)
            {
                SongChangePopupEnabledChanged?.Invoke(null, EventArgs.Empty);
            }
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
