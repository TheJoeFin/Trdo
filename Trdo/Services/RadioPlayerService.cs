using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Linq;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Trdo.Services;

public sealed partial class RadioPlayerService : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly DispatcherQueue? _uiQueue;
    private readonly StreamWatchdogService _watchdog;
    private double _volume = 0.5;
    private const string VolumeKey = "RadioVolume";
    private const string WatchdogEnabledKey = "WatchdogEnabled";
    private const string IsPlayingKey = "RadioIsPlaying";
    private const string CurrentStreamUrlKey = "RadioCurrentStreamUrl";
    private string? _streamUrl;
    private bool _isInternalStateChange;
    private readonly bool _isComServerMode;

    public static RadioPlayerService Instance { get; } = new();

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<double>? VolumeChanged;

    public bool IsPlaying
    {
        get
        {
            // First check the MediaPlayer state
            bool localIsPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

            // Also sync with shared state storage
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsPlayingKey, out object? storedValue))
                {
                    bool storedIsPlaying = storedValue is bool b && b;
                    // If there's a mismatch, the shared state wins (other process may have changed it)
                    if (storedIsPlaying != localIsPlaying)
                    {
                        Debug.WriteLine($"[RadioPlayerService] IsPlaying state mismatch - Shared: {storedIsPlaying}, Local: {localIsPlaying}, using Shared");

                        // Return the shared state value
                        // This ensures that if another process (widget) changed the state,
                        // we report the correct state
                        return storedIsPlaying;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Failed to read shared IsPlaying state: {ex.Message}");
            }

            Debug.WriteLine($"[RadioPlayerService] IsPlaying getter: {localIsPlaying}, PlaybackState: {_player.PlaybackSession.PlaybackState}");
            return localIsPlaying;
        }
    }

    /// <summary>
    /// Gets the actual local MediaPlayer state without checking shared storage.
    /// Used for syncing the MediaPlayer to match shared state.
    /// </summary>
    public bool IsLocalMediaPlayerPlaying
    {
        get
        {
            bool isPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            Debug.WriteLine($"[RadioPlayerService] IsLocalMediaPlayerPlaying: {isPlaying}");
            return isPlaying;
        }
    }

    public string? StreamUrl
    {
        get
        {
            // Sync with shared state
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out object? storedUrl))
                {
                    string? sharedUrl = storedUrl as string;
                    if (!string.IsNullOrEmpty(sharedUrl) && sharedUrl != _streamUrl)
                    {
                        Debug.WriteLine($"[RadioPlayerService] StreamUrl mismatch - Shared: {sharedUrl}, Local: {_streamUrl}");
                        _streamUrl = sharedUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Failed to read shared StreamUrl: {ex.Message}");
            }

            Debug.WriteLine($"[RadioPlayerService] StreamUrl getter: {_streamUrl}");
            return _streamUrl;
        }
    }

    public StreamWatchdogService Watchdog => _watchdog;

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(_volume - value) < 0.0001) return;
            Debug.WriteLine($"[RadioPlayerService] Setting Volume from {_volume} to {value}");
            _volume = value;
            _player.Volume = _volume;
            try
            {
                ApplicationData.Current.LocalSettings.Values[VolumeKey] = _volume;
            }
            catch { }
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    public bool WatchdogEnabled
    {
        get => _watchdog.IsEnabled;
        set
        {
            Debug.WriteLine($"[RadioPlayerService] Setting WatchdogEnabled to {value}");
            _watchdog.IsEnabled = value;
            try
            {
                ApplicationData.Current.LocalSettings.Values[WatchdogEnabledKey] = value;
            }
            catch { }
        }
    }

    private RadioPlayerService()
    {
        Debug.WriteLine("=== RadioPlayerService Constructor START ===");

        // Check if we're running as COM server (widget process)
        string[] cmdLineArgs = Environment.GetCommandLineArgs();
        _isComServerMode = cmdLineArgs.Contains("-RegisterProcessAsComServer");
        Debug.WriteLine($"[RadioPlayerService] COM Server Mode: {_isComServerMode}");

        _uiQueue = DispatcherQueue.GetForCurrentThread();
        Debug.WriteLine($"[RadioPlayerService] DispatcherQueue obtained: {_uiQueue != null}");

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = false,
            IsLoopingEnabled = false,
            Volume = _volume
        };
        Debug.WriteLine($"[RadioPlayerService] MediaPlayer created with Volume={_volume}, AutoPlay=false");

        // Enable System Media Transport Controls (SMTC)
        // This allows the media player to be controlled system-wide
        SystemMediaTransportControls smtc = _player.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsStopEnabled = true;
        Debug.WriteLine("[RadioPlayerService] System Media Transport Controls enabled");

        _player.PlaybackSession.PlaybackStateChanged += (_, _) =>
        {
            bool isPlaying;
            MediaPlaybackState currentState;
            try
            {
                currentState = _player.PlaybackSession.PlaybackState;
                isPlaying = currentState == MediaPlaybackState.Playing;
                Debug.WriteLine($"[RadioPlayerService] PlaybackStateChanged event: IsPlaying={isPlaying}, State={currentState}, IsInternalChange={_isInternalStateChange}, ComServerMode={_isComServerMode}");

                // Only update shared state if we're the main app (not COM server)
                // This prevents the widget process from interfering with state
                if (!_isComServerMode)
                {
                    // Update shared state storage so other processes can see this change
                    try
                    {
                        ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = isPlaying;
                        Debug.WriteLine($"[RadioPlayerService] Updated shared IsPlaying state to: {isPlaying}");
                    }
                    catch (Exception stateEx)
                    {
                        Debug.WriteLine($"[RadioPlayerService] Failed to update shared state: {stateEx.Message}");
                    }
                }

                // Update SMTC playback status
                try
                {
                    MediaPlaybackStatus smtcStatus = currentState switch
                    {
                        MediaPlaybackState.Playing => Windows.Media.MediaPlaybackStatus.Playing,
                        MediaPlaybackState.Paused => Windows.Media.MediaPlaybackStatus.Paused,
                        MediaPlaybackState.None => Windows.Media.MediaPlaybackStatus.Stopped,
                        _ => Windows.Media.MediaPlaybackStatus.Changing
                    };
                    smtc.PlaybackStatus = smtcStatus;
                    Debug.WriteLine($"[RadioPlayerService] SMTC playback status updated to: {smtcStatus}");
                }
                catch (Exception smtcEx)
                {
                    Debug.WriteLine($"[RadioPlayerService] Failed to update SMTC status: {smtcEx.Message}");
                }

                // If state change was not initiated internally (e.g., from hardware buttons),
                // notify the watchdog of user intention
                if (!_isInternalStateChange)
                {
                    Debug.WriteLine("[RadioPlayerService] External state change detected (likely hardware button or widget)");
                    if (currentState == MediaPlaybackState.Playing)
                    {
                        _watchdog?.NotifyUserIntentionToPlay();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play");
                    }
                    else if (currentState == MediaPlaybackState.Paused)
                    {
                        // Only notify pause intent if explicitly paused (not buffering, opening, or other states)
                        _watchdog?.NotifyUserIntentionToPause();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to pause");
                    }
                    // For other states (Buffering, Opening, None), don't change watchdog intent
                    // This allows the watchdog to recover if a stream stops unexpectedly
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] EXCEPTION in PlaybackStateChanged: {ex.Message}");
                return;
            }
            TryEnqueueOnUi(() => PlaybackStateChanged?.Invoke(this, isPlaying));
        };

        _watchdog = new StreamWatchdogService(this);
        Debug.WriteLine("[RadioPlayerService] StreamWatchdogService created");

        LoadSettings();

        // Load shared state from storage
        LoadSharedState();

        Debug.WriteLine("=== RadioPlayerService Constructor END ===");
    }

    private void LoadSharedState()
    {
        Debug.WriteLine("[RadioPlayerService] LoadSharedState START");
        try
        {
            // Load current stream URL from shared state
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out object? urlValue))
            {
                _streamUrl = urlValue as string;
                Debug.WriteLine($"[RadioPlayerService] Loaded shared stream URL: {_streamUrl}");

                // If we have a stream URL, initialize the player
                // But in COM server mode, DON'T create MediaSource or start playback
                // Only the main app should play audio
                if (!string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
                {
                    try
                    {
                        Uri uri = new(_streamUrl);
                        _player.Source = MediaSource.CreateFromUri(uri);
                        Debug.WriteLine($"[RadioPlayerService] Initialized MediaSource from shared state");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RadioPlayerService] Failed to initialize MediaSource from shared URL: {ex.Message}");
                    }
                }
                else if (_isComServerMode)
                {
                    Debug.WriteLine($"[RadioPlayerService] COM server mode - skipping MediaSource initialization");
                }
            }

            // Load playing state from shared state
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsPlayingKey, out object? playingValue))
            {
                bool sharedIsPlaying = playingValue is bool b && b;
                Debug.WriteLine($"[RadioPlayerService] Loaded shared IsPlaying state: {sharedIsPlaying}");

                // If shared state says we should be playing, start playback
                // But ONLY in main app mode, NEVER in COM server mode
                if (sharedIsPlaying && !string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
                {
                    try
                    {
                        Debug.WriteLine($"[RadioPlayerService] Resuming playback from shared state (main app mode)");
                        _player.Play();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RadioPlayerService] Failed to resume playback: {ex.Message}");
                    }
                }
                else if (_isComServerMode)
                {
                    Debug.WriteLine($"[RadioPlayerService] COM server mode - skipping playback resume");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] EXCEPTION in LoadSharedState: {ex.Message}");
        }
        Debug.WriteLine("[RadioPlayerService] LoadSharedState END");
    }

    private void LoadSettings()
    {
        Debug.WriteLine("[RadioPlayerService] LoadSettings START");
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(VolumeKey, out object? v))
            {
                double parsed = v switch
                {
                    double d => d,
                    string s when double.TryParse(s, out double d2) => d2,
                    _ => _volume
                };
                _volume = Math.Clamp(parsed, 0, 1);
                _player.Volume = _volume;
                Debug.WriteLine($"[RadioPlayerService] Loaded volume from settings: {_volume}");
            }

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(WatchdogEnabledKey, out object? w))
            {
                bool watchdogEnabled = w switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out bool b2) => b2,
                    _ => true
                };
                _watchdog.IsEnabled = watchdogEnabled;
                Debug.WriteLine($"[RadioPlayerService] Loaded watchdog enabled from settings: {watchdogEnabled}");
            }
            else
            {
                _watchdog.IsEnabled = true;
                Debug.WriteLine("[RadioPlayerService] No saved watchdog setting, defaulting to enabled");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] EXCEPTION in LoadSettings: {ex.Message}");
        }
        Debug.WriteLine("[RadioPlayerService] LoadSettings END");
    }

    /// <summary>
    /// Initialize the player with a stream URL (first-time setup only)
    /// </summary>
    public void Initialize(string streamUrl)
    {
        Debug.WriteLine($"=== Initialize START ===");
        Debug.WriteLine($"[RadioPlayerService] Initialize called with URL: {streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current _streamUrl: {_streamUrl}");

        if (!string.IsNullOrWhiteSpace(_streamUrl))
        {
            // Already initialized, use SetStreamUrl instead
            Debug.WriteLine("[RadioPlayerService] Already initialized, skipping Initialize");
            Debug.WriteLine($"=== Initialize END (already initialized) ===");
            return;
        }

        Debug.WriteLine("[RadioPlayerService] Calling SetStreamUrl...");
        SetStreamUrl(streamUrl);
        Debug.WriteLine($"=== Initialize END ===");
    }

    /// <summary>
    /// Set or change the stream URL. This will prepare the stream for playback.
    /// </summary>
    public void SetStreamUrl(string streamUrl)
    {
        Debug.WriteLine($"=== SetStreamUrl START ===");
        Debug.WriteLine($"[RadioPlayerService] SetStreamUrl called with: {streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Previous URL: {_streamUrl}");

        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            Debug.WriteLine("[RadioPlayerService] ERROR: Stream URL is empty");
            throw new ArgumentException("Stream URL cannot be empty", nameof(streamUrl));
        }

        Uri uri = new(streamUrl); // Will throw if invalid URL
        Debug.WriteLine($"[RadioPlayerService] URI created successfully: {uri}");

        // Update the stream URL
        _streamUrl = streamUrl;

        // Save to shared state so other processes can see this
        try
        {
            ApplicationData.Current.LocalSettings.Values[CurrentStreamUrlKey] = _streamUrl;
            Debug.WriteLine($"[RadioPlayerService] Updated shared StreamUrl to: {_streamUrl}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Failed to save shared StreamUrl: {ex.Message}");
        }

        Debug.WriteLine($"[RadioPlayerService] _streamUrl updated to: {_streamUrl}");

        // Configure player for live streaming
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.RealTimePlayback = true;
        Debug.WriteLine("[RadioPlayerService] Player configured for live streaming");

        // Dispose old source if exists
        if (_player.Source is MediaSource oldMedia)
        {
            Debug.WriteLine("[RadioPlayerService] Disposing old MediaSource");
            oldMedia.Reset();
            oldMedia.Dispose();
        }

        // Set new media source
        Debug.WriteLine($"[RadioPlayerService] Creating new MediaSource from URI: {uri}");
        _player.Source = MediaSource.CreateFromUri(uri);
        Debug.WriteLine("[RadioPlayerService] New MediaSource set on player");
        Debug.WriteLine($"=== SetStreamUrl END ===");
    }

    /// <summary>
    /// Update the System Media Transport Controls display information
    /// </summary>
    /// <param name="stationName">The name of the current radio station</param>
    public void UpdateNowPlaying(string stationName)
    {
        try
        {
            SystemMediaTransportControlsDisplayUpdater displayUpdater = _player.SystemMediaTransportControls.DisplayUpdater;
            displayUpdater.Type = MediaPlaybackType.Music;
            displayUpdater.MusicProperties.Title = stationName;
            displayUpdater.MusicProperties.Artist = "Trdo Radio";
            displayUpdater.Update();
            Debug.WriteLine($"[RadioPlayerService] Updated SMTC display: {stationName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Failed to update SMTC display: {ex.Message}");
        }
    }

    /// <summary>
    /// Start playback of the current stream
    /// </summary>
    public void Play()
    {
        Debug.WriteLine($"=== Play START ===");
        Debug.WriteLine($"[RadioPlayerService] Play called (ComServerMode={_isComServerMode})");

        // In COM server mode (widget), we only update shared state
        // The main app will detect the change and start playback
        if (_isComServerMode)
        {
            Debug.WriteLine("[RadioPlayerService] COM server mode - updating shared state only");
            try
            {
                ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = true;
                Debug.WriteLine("[RadioPlayerService] Updated shared state to Playing (widget request)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Failed to update shared state: {ex.Message}");
            }
            Debug.WriteLine($"=== Play END (COM server mode) ===");
            return;
        }

        // Main app mode - actually play audio
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {_player.PlaybackSession.PlaybackState}");
        Debug.WriteLine($"[RadioPlayerService] Player.Source is null: {_player.Source == null}");

        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            // Check if there's a shared stream URL we should use
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out object? urlValue))
                {
                    string? sharedUrl = urlValue as string;
                    if (!string.IsNullOrEmpty(sharedUrl))
                    {
                        Debug.WriteLine($"[RadioPlayerService] Loading stream URL from shared state: {sharedUrl}");
                        _streamUrl = sharedUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Failed to read shared stream URL: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(_streamUrl))
            {
                Debug.WriteLine("[RadioPlayerService] ERROR: No stream URL set");
                throw new InvalidOperationException("No stream URL set. Call SetStreamUrl first.");
            }
        }

        try
        {
            // Ensure we have a fresh media source
            if (_player.Source == null)
            {
                Debug.WriteLine("[RadioPlayerService] Player.Source is null, creating new MediaSource");
                Uri uri = new(_streamUrl);
                _player.Source = MediaSource.CreateFromUri(uri);
                Debug.WriteLine($"[RadioPlayerService] Created new MediaSource from URL: {_streamUrl}");
            }
            else
            {
                Debug.WriteLine($"[RadioPlayerService] Player.Source exists, current state: {(_player.Source as MediaSource)?.State}");
            }

            Debug.WriteLine("[RadioPlayerService] Calling _player.Play()...");
            _isInternalStateChange = true;
            _player.Play();
            _isInternalStateChange = false;
            Debug.WriteLine("[RadioPlayerService] _player.Play() called successfully");

            _watchdog.NotifyUserIntentionToPlay();
            Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play");

            // Note: PlaybackStateChanged event will update shared state automatically
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] EXCEPTION in Play: {ex.Message}");
            Debug.WriteLine($"[RadioPlayerService] Exception details: {ex}");
            Debug.WriteLine("[RadioPlayerService] Re-creating media source and trying again...");

            // Re-create the media source and try again
            try
            {
                Uri uri = new(_streamUrl);
                _player.Source = MediaSource.CreateFromUri(uri);
                Debug.WriteLine($"[RadioPlayerService] Created new MediaSource from URL: {_streamUrl}");

                _isInternalStateChange = true;
                _player.Play();
                _isInternalStateChange = false;
                Debug.WriteLine("[RadioPlayerService] _player.Play() called successfully (retry)");

                _watchdog.NotifyUserIntentionToPlay();
                Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play");
            }
            catch (Exception retryEx)
            {
                _isInternalStateChange = false;
                Debug.WriteLine($"[RadioPlayerService] EXCEPTION on retry: {retryEx.Message}");
                throw;
            }
        }

        Debug.WriteLine($"=== Play END ===");
    }

    /// <summary>
    /// Stop playback and clean up resources
    /// </summary>
    public void Pause()
    {
        Debug.WriteLine($"=== Pause START ===");
        Debug.WriteLine($"[RadioPlayerService] Pause called (ComServerMode={_isComServerMode})");

        // In COM server mode (widget), we only update shared state
        // The main app will detect the change and pause playback
        if (_isComServerMode)
        {
            Debug.WriteLine("[RadioPlayerService] COM server mode - updating shared state only");
            try
            {
                ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = false;
                Debug.WriteLine("[RadioPlayerService] Updated shared state to Paused (widget request)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Failed to update shared state: {ex.Message}");
            }
            Debug.WriteLine($"=== Pause END (COM server mode) ===");
            return;
        }

        // Main app mode - actually pause audio
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {_player.PlaybackSession.PlaybackState}");

        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            // Check shared state for stream URL
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out object? urlValue))
                {
                    _streamUrl = urlValue as string;
                    Debug.WriteLine($"[RadioPlayerService] Loaded stream URL from shared state for pause: {_streamUrl}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RadioPlayerService] Exception while loading stream URL from shared state: {ex.Message}");
                Debug.WriteLine($"[RadioPlayerService] Exception details: {ex}");
            }

        }

        // Always try to pause, even if we don't have a stream URL
        // This ensures we stop any MediaPlayer that might be playing
        try
        {
            Debug.WriteLine("[RadioPlayerService] Calling _player.Pause()...");
            _isInternalStateChange = true;
            _player.Pause();
            _isInternalStateChange = false;
            Debug.WriteLine("[RadioPlayerService] _player.Pause() called successfully");

            // Clean up the media source for live streams
            if (_player.Source is MediaSource media)
            {
                Debug.WriteLine("[RadioPlayerService] Disposing MediaSource");
                media.Reset();
                media.Dispose();
            }
            _player.Source = null;
            Debug.WriteLine("[RadioPlayerService] Player.Source set to null");

            _watchdog.NotifyUserIntentionToPause();
            Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to pause");

            // Note: PlaybackStateChanged event will update shared state automatically

            // DO NOT prepare the stream here - let Play() or SetStreamUrl() handle it
            // The previous code was creating a MediaSource with the current URL,
            // but if the user then selects a different station, the MediaSource
            // would be in "Opening" state with the OLD URL, preventing the new station from playing
            Debug.WriteLine("[RadioPlayerService] Stream cleanup complete, ready for next operation");
        }
        catch (Exception ex)
        {
            _isInternalStateChange = false;
            Debug.WriteLine($"[RadioPlayerService] EXCEPTION in Pause: {ex.Message}");
            Debug.WriteLine($"[RadioPlayerService] Exception details: {ex}");
        }

        Debug.WriteLine($"=== Pause END ===");
    }

    /// <summary>
    /// Toggle between play and pause
    /// </summary>
    public void TogglePlayPause()
    {
        Debug.WriteLine($"=== TogglePlayPause START ===");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {IsPlaying}");
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");

        if (IsPlaying)
        {
            Debug.WriteLine("[RadioPlayerService] Is playing, calling Pause()");
            Pause();
        }
        else
        {
            Debug.WriteLine("[RadioPlayerService] Not playing, calling Play()");
            Play();
        }

        Debug.WriteLine($"=== TogglePlayPause END ===");
    }

    private void TryEnqueueOnUi(DispatcherQueueHandler action)
    {
        if (_uiQueue is null)
        {
            action();
            return;
        }

        if (_uiQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _uiQueue.TryEnqueue(action);
        }
    }

    public void Dispose()
    {
        Debug.WriteLine("[RadioPlayerService] Dispose called");
        _watchdog.Dispose();

        if (_player.Source is MediaSource media)
        {
            media.Reset();
            media.Dispose();
        }

        _player.Dispose();
        Debug.WriteLine("[RadioPlayerService] Disposed");
    }
}
