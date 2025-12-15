using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Trdo.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Trdo.Services;

public sealed partial class RadioPlayerService : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly DispatcherQueue _uiQueue;
    private readonly StreamWatchdogService _watchdog;
    private readonly StreamMetadataService _metadataService;
    private readonly SystemMediaTransportControls? _systemMediaControls;
    private readonly HttpClient _httpClient;
    private double _volume = 0.5;
    private const string VolumeKey = "RadioVolume";
    private const string WatchdogEnabledKey = "WatchdogEnabled";
    private string? _streamUrl;
    private string? _currentStationName;
    private string? _currentStationFaviconUrl;
    private string? _currentAlbumArtUrl;
    private bool _isInternalStateChange;
    private bool _wasExternalPause;
    private System.Threading.Timer? _smtcUpdateTimer;
    private bool _smtcUpdatePending;
    private readonly object _smtcUpdateLock = new();
    private System.Threading.Timer? _internalStateChangeTimer;
    private DateTime _lastExternalPauseRecovery = DateTime.MinValue;
    private bool _hasPlayedOnce;

    public static RadioPlayerService Instance { get; } = new();

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? BufferingStateChanged;
    public event EventHandler<StreamMetadata>? StreamMetadataChanged;

    public bool IsPlaying
    {
        get
        {
            bool isPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            Debug.WriteLine($"[RadioPlayerService] IsPlaying getter: {isPlaying}, PlaybackState: {_player.PlaybackSession.PlaybackState}");
            return isPlaying;
        }
    }

    public bool IsBuffering
    {
        get
        {
            try
            {
                MediaPlaybackState state = _player.PlaybackSession.PlaybackState;
                bool isBuffering = state is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
                Debug.WriteLine($"[RadioPlayerService] IsBuffering getter: {isBuffering}, PlaybackState: {state}");
                return isBuffering;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? StreamUrl
    {
        get
        {
            Debug.WriteLine($"[RadioPlayerService] StreamUrl getter: {_streamUrl}");
            return _streamUrl;
        }
    }

    /// <summary>
    /// Gets the buffering progress as a value between 0 and 1.
    /// For live streams, this can help detect if the stream is actually delivering data.
    /// </summary>
    public double BufferingProgress
    {
        get
        {
            try
            {
                return _player.PlaybackSession.BufferingProgress;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the current playback position.
    /// For live streams, this can help detect if audio is actually progressing.
    /// </summary>
    public TimeSpan Position
    {
        get
        {
            try
            {
                return _player.PlaybackSession.Position;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }

    public StreamWatchdogService Watchdog => _watchdog;

    /// <summary>
    /// Gets the current stream metadata (now playing information).
    /// </summary>
    public StreamMetadata CurrentMetadata => _metadataService.CurrentMetadata;

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

        _uiQueue = DispatcherQueue.GetForCurrentThread();
        Debug.WriteLine($"[RadioPlayerService] DispatcherQueue obtained: {_uiQueue != null}");

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        Debug.WriteLine("[RadioPlayerService] HttpClient created for album art downloads");

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = false,
            IsLoopingEnabled = false,
            Volume = _volume
        };
        Debug.WriteLine($"[RadioPlayerService] MediaPlayer created with Volume={_volume}, AutoPlay=false");

        _player.PlaybackSession.PlaybackStateChanged += (_, _) =>
        {
            bool isPlaying;
            bool isBuffering;
            MediaPlaybackState currentState;
            try
            {
                currentState = _player.PlaybackSession.PlaybackState;
                isPlaying = currentState == MediaPlaybackState.Playing;
                isBuffering = currentState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
                Debug.WriteLine($"[RadioPlayerService] PlaybackStateChanged event: IsPlaying={isPlaying}, IsBuffering={isBuffering}, State={currentState}, IsInternalChange={_isInternalStateChange}");

                // If state change was not initiated internally (e.g., from hardware buttons),
                // notify the watchdog of user intention
                if (!_isInternalStateChange)
                {
                    Debug.WriteLine("[RadioPlayerService] External state change detected (likely hardware button)");
                    if (currentState == MediaPlaybackState.Playing)
                    {
                        _watchdog.NotifyUserIntentionToPlay();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play (hardware button)");

                        // Mark that an external play was triggered
                        // The Play() method will handle MediaSource recreation if needed
                        if (_wasExternalPause)
                        {
                            Debug.WriteLine("[RadioPlayerService] External play detected after external pause - will be handled by Play() method");
                        }
                    }
                    else if (currentState == MediaPlaybackState.Paused)
                    {
                        // Only notify pause intent if explicitly paused (not buffering, opening, or other states)
                        _watchdog.NotifyUserIntentionToPause();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to pause (hardware button)");

                        // Mark that this was an external pause
                        _wasExternalPause = true;
                        Debug.WriteLine("[RadioPlayerService] Marked as external pause - will refresh stream on next play");

                        // Stop metadata polling when paused
                        _metadataService.StopPolling();
                        Debug.WriteLine("[RadioPlayerService] Stopped metadata polling after external pause");
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
            TryEnqueueOnUi(() =>
            {
                PlaybackStateChanged?.Invoke(this, isPlaying);
                BufferingStateChanged?.Invoke(this, isBuffering);
                ScheduleSystemMediaTransportControlsUpdate();
            });
        };

        _watchdog = new StreamWatchdogService(this);
        Debug.WriteLine("[RadioPlayerService] StreamWatchdogService created");

        _metadataService = new StreamMetadataService();
        _metadataService.MetadataChanged += (_, metadata) =>
        {
            Debug.WriteLine($"[RadioPlayerService] Metadata changed: {metadata.DisplayText}");
            TryEnqueueOnUi(() =>
            {
                StreamMetadataChanged?.Invoke(this, metadata);
                ScheduleSystemMediaTransportControlsUpdate();
            });
        };
        Debug.WriteLine("[RadioPlayerService] StreamMetadataService created");

        // Initialize SystemMediaTransportControls
        try
        {
            _systemMediaControls = _player.SystemMediaTransportControls;
            if (_systemMediaControls != null)
            {
                _systemMediaControls.IsEnabled = true;
                _systemMediaControls.IsPlayEnabled = true;
                _systemMediaControls.IsPauseEnabled = true;
                _systemMediaControls.IsStopEnabled = false;
                _systemMediaControls.IsNextEnabled = false;
                _systemMediaControls.IsPreviousEnabled = false;

                _systemMediaControls.ButtonPressed += OnSystemMediaButtonPressed;
                Debug.WriteLine("[RadioPlayerService] SystemMediaTransportControls initialized");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Failed to initialize SystemMediaTransportControls: {ex.Message}");
        }

        LoadSettings();

        Debug.WriteLine("=== RadioPlayerService Constructor END ===");
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

        // Mark this as internal so we don't trigger external pause detection
        SetInternalStateChange(true);
        SetStreamUrl(streamUrl);

        // Allow time for the MediaSource to fully initialize before first play
        // This prevents the initial play from causing cascading state changes
        Debug.WriteLine("[RadioPlayerService] MediaSource created and ready for playback");

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

        // If changing stations, reset the first play flag
        if (_streamUrl != streamUrl)
        {
            _hasPlayedOnce = false;
            Debug.WriteLine("[RadioPlayerService] Station changed - reset first play flag");
        }

        // Update the stream URL
        _streamUrl = streamUrl;
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

        // Update SMTC with new station
        ScheduleSystemMediaTransportControlsUpdate();

        Debug.WriteLine($"=== SetStreamUrl END ===");
    }

    /// <summary>
    /// Set the current station name for display in system media controls.
    /// </summary>
    public void SetStationName(string stationName)
    {
        Debug.WriteLine($"[RadioPlayerService] Setting station name to: {stationName}");
        _currentStationName = stationName;
        ScheduleSystemMediaTransportControlsUpdate();
    }

    /// <summary>
    /// Set the current station favicon URL for display in system media controls.
    /// </summary>
    public void SetStationFavicon(string? faviconUrl)
    {
        Debug.WriteLine($"[RadioPlayerService] Setting station favicon to: {faviconUrl}");
        _currentStationFaviconUrl = faviconUrl;
        ScheduleSystemMediaTransportControlsUpdate();
    }

    /// <summary>
    /// Start playback of the current stream
    /// </summary>
    public void Play()
    {
        Debug.WriteLine($"=== Play START ===");
        Debug.WriteLine($"[RadioPlayerService] Play called");
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {_player.PlaybackSession.PlaybackState}");
        Debug.WriteLine($"[RadioPlayerService] Player.Source is null: {_player.Source == null}");
        Debug.WriteLine($"[RadioPlayerService] Has played once: {_hasPlayedOnce}");
        Debug.WriteLine($"[RadioPlayerService] Was external pause: {_wasExternalPause}");

        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            Debug.WriteLine("[RadioPlayerService] ERROR: No stream URL set");
            throw new InvalidOperationException("No stream URL set. Call SetStreamUrl first.");
        }

        try
        {
            // Only recreate MediaSource if:
            // 1. First play and no source exists, OR
            // 2. Following an external pause (to ensure live stream position)
            bool needsRecreation = (!_hasPlayedOnce && _player.Source == null) || _wasExternalPause;

            if (needsRecreation)
            {
                if (_wasExternalPause)
                {
                    Debug.WriteLine("[RadioPlayerService] Recreating MediaSource after external pause to seek to live position");
                }
                else
                {
                    Debug.WriteLine("[RadioPlayerService] First play - creating MediaSource");
                }

                // Dispose old source if exists
                if (_player.Source is MediaSource oldMedia)
                {
                    Debug.WriteLine("[RadioPlayerService] Disposing existing MediaSource");
                    oldMedia.Reset();
                    oldMedia.Dispose();
                }

                // Create fresh MediaSource
                Uri uri = new(_streamUrl);
                _player.Source = MediaSource.CreateFromUri(uri);
                Debug.WriteLine($"[RadioPlayerService] Created new MediaSource from URL: {_streamUrl}");

                _hasPlayedOnce = true;
            }
            else
            {
                Debug.WriteLine("[RadioPlayerService] Reusing existing MediaSource - resuming playback");
            }

            Debug.WriteLine("[RadioPlayerService] Calling _player.Play()...");
            SetInternalStateChange(true);
            _player.Play();
            Debug.WriteLine("[RadioPlayerService] _player.Play() called successfully");

            // Clear the external pause flag
            _wasExternalPause = false;
            Debug.WriteLine("[RadioPlayerService] Cleared external pause flag");

            _watchdog.NotifyUserIntentionToPlay();
            Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play");

            // Start metadata polling
            _metadataService.StartPolling(_streamUrl);
            Debug.WriteLine("[RadioPlayerService] Started metadata polling");
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

                SetInternalStateChange(true);
                _player.Play();
                Debug.WriteLine("[RadioPlayerService] _player.Play() called successfully (retry)");

                _wasExternalPause = false;
                _hasPlayedOnce = true;
                Debug.WriteLine("[RadioPlayerService] Cleared external pause flag (retry)");

                _watchdog.NotifyUserIntentionToPlay();
                Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play");

                // Start metadata polling
                _metadataService.StartPolling(_streamUrl);
                Debug.WriteLine("[RadioPlayerService] Started metadata polling (retry)");
            }
            catch (Exception retryEx)
            {
                SetInternalStateChange(false);
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
        Debug.WriteLine($"[RadioPlayerService] Pause called");
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {_player.PlaybackSession.PlaybackState}");

        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            Debug.WriteLine("[RadioPlayerService] No stream URL set, nothing to pause");
            Debug.WriteLine($"=== Pause END (no URL) ===");
            return;
        }

        try
        {
            Debug.WriteLine("[RadioPlayerService] Calling _player.Pause()...");
            SetInternalStateChange(true);
            _player.Pause();
            Debug.WriteLine("[RadioPlayerService] _player.Pause() called successfully");

            // Clear the external pause flag since this is an internal pause
            _wasExternalPause = false;
            Debug.WriteLine("[RadioPlayerService] Cleared external pause flag (internal pause)");

            _watchdog.NotifyUserIntentionToPause();
            Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to pause");

            // Stop metadata polling
            _metadataService.StopPolling();
            Debug.WriteLine("[RadioPlayerService] Stopped metadata polling");

            // Keep the media source intact so media controls remain available
            // The Play() method will dispose and recreate it to ensure fresh stream
            Debug.WriteLine("[RadioPlayerService] Media source kept intact for media controls");
        }
        catch (Exception ex)
        {
            SetInternalStateChange(false);
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

    private void OnSystemMediaButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        Debug.WriteLine($"[RadioPlayerService] System media button pressed: {args.Button}");

        TryEnqueueOnUi(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Debug.WriteLine("[RadioPlayerService] Play button pressed from system controls");
                    Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Debug.WriteLine("[RadioPlayerService] Pause button pressed from system controls");
                    Pause();
                    break;
                default:
                    Debug.WriteLine($"[RadioPlayerService] Unhandled button: {args.Button}");
                    break;
            }
                });
            }

            /// <summary>
            /// Sets the internal state change flag with a timer to automatically clear it.
            /// This ensures the flag remains true long enough to cover asynchronous state changes.
            /// </summary>
            /// <param name="isInternal">True to mark state changes as internal, false to clear immediately</param>
            private void SetInternalStateChange(bool isInternal)
            {
                _isInternalStateChange = isInternal;

                if (isInternal)
                {
                    Debug.WriteLine("[RadioPlayerService] Internal state change flag SET (will auto-clear in 1000ms)");

                    // Clear any existing timer
                    _internalStateChangeTimer?.Dispose();

                    // Set a timer to clear the flag after 1000ms
                    // This gives enough time for all async state changes from Play()/Pause() to complete
                    // Increased from 500ms to 1000ms to better handle slower network connections
                    _internalStateChangeTimer = new System.Threading.Timer(
                        callback: _ =>
                        {
                            _isInternalStateChange = false;
                            Debug.WriteLine("[RadioPlayerService] Internal state change flag AUTO-CLEARED");
                        },
                        state: null,
                        dueTime: 1000, // 1000ms to cover all async state transitions including network delays
                        period: System.Threading.Timeout.Infinite
                    );
                }
                else
                {
                    Debug.WriteLine("[RadioPlayerService] Internal state change flag CLEARED immediately");
                    _internalStateChangeTimer?.Dispose();
                    _internalStateChangeTimer = null;
                }
            }

            /// <summary>
            /// Schedules a debounced update to the System Media Transport Controls.
        /// Multiple rapid calls will be coalesced into a single update after 100ms of inactivity.
        /// </summary>
        private void ScheduleSystemMediaTransportControlsUpdate()
        {
            lock (_smtcUpdateLock)
            {
                // Mark that an update is pending
                _smtcUpdatePending = true;

                // Reset the timer - this will delay the update by another 100ms
                _smtcUpdateTimer?.Dispose();
                _smtcUpdateTimer = new System.Threading.Timer(
                    callback: _ => ExecuteSystemMediaTransportControlsUpdate(),
                    state: null,
                    dueTime: 100, // 100ms delay
                    period: System.Threading.Timeout.Infinite // Don't repeat
                );

                Debug.WriteLine("[RadioPlayerService] SMTC update scheduled (debounced)");
            }
        }

        /// <summary>
        /// Executes the actual System Media Transport Controls update on the UI thread.
        /// </summary>
        private void ExecuteSystemMediaTransportControlsUpdate()
        {
            lock (_smtcUpdateLock)
            {
                if (!_smtcUpdatePending)
                {
                    return;
                }

                _smtcUpdatePending = false;
                Debug.WriteLine("[RadioPlayerService] Executing debounced SMTC update");
            }

            // Execute the update on the UI thread
            TryEnqueueOnUi(() =>
            {
                UpdateSystemMediaTransportControls();
            });
        }

        private void UpdateSystemMediaTransportControls()
    {
        if (_systemMediaControls == null)
            return;

        try
        {
            // Get the display updater
            SystemMediaTransportControlsDisplayUpdater updater = _systemMediaControls.DisplayUpdater;

            // Always set the type to Music for radio stations
            updater.Type = MediaPlaybackType.Music;

            // Update playback status
            _systemMediaControls.PlaybackStatus = IsPlaying
                ? MediaPlaybackStatus.Playing
                : MediaPlaybackStatus.Paused;

            StreamMetadata metadata = CurrentMetadata;

            // Set artist and title from metadata if available
            if (metadata.HasMetadata)
            {
                // Set artist - prefer metadata artist, fall back to station name
                if (!string.IsNullOrWhiteSpace(metadata.Artist))
                {
                    updater.MusicProperties.Artist = metadata.Artist;
                    // Only set AlbumArtist when we don't have artist info, otherwise it takes precedence
                    updater.MusicProperties.AlbumArtist = string.Empty;
                }
                else
                {
                    updater.MusicProperties.Artist = _currentStationName ?? "Radio Station";
                    updater.MusicProperties.AlbumArtist = string.Empty;
                }

                // Set title from metadata
                if (!string.IsNullOrWhiteSpace(metadata.Title))
                {
                    updater.MusicProperties.Title = metadata.Title;
                }
                else if (!string.IsNullOrWhiteSpace(metadata.StreamTitle))
                {
                    updater.MusicProperties.Title = metadata.StreamTitle;
                }
                else
                {
                    updater.MusicProperties.Title = "Now Playing";
                }

                // Set album title to station name for additional context
                updater.MusicProperties.AlbumTitle = _currentStationName ?? "Radio Station";

                Debug.WriteLine($"[RadioPlayerService] SMTC updated with metadata - Artist: {updater.MusicProperties.Artist}, Title: {updater.MusicProperties.Title}, Album: {updater.MusicProperties.AlbumTitle}");
            }
            else
            {
                // No metadata available, show station name
                updater.MusicProperties.Artist = _currentStationName ?? "Radio Station";
                updater.MusicProperties.Title = "Streaming...";
                updater.MusicProperties.AlbumArtist = string.Empty;
                updater.MusicProperties.AlbumTitle = string.Empty;
                Debug.WriteLine($"[RadioPlayerService] SMTC updated with station name: {_currentStationName}");
            }

            // Handle album art with proper priority: metadata album art > station favicon > none
            // Use async/await pattern to handle fallback properly
            _ = Task.Run(async () =>
            {
                bool thumbnailSet = false;

                // Try metadata album art first
                if (!string.IsNullOrWhiteSpace(metadata.AlbumArtUrl))
                {
                    if (metadata.AlbumArtUrl != _currentAlbumArtUrl)
                    {
                        Debug.WriteLine($"[RadioPlayerService] Attempting to set album art from metadata: {metadata.AlbumArtUrl}");
                        thumbnailSet = await SetAlbumArtAsync(updater, metadata.AlbumArtUrl);

                        if (thumbnailSet)
                        {
                            _currentAlbumArtUrl = metadata.AlbumArtUrl;
                            Debug.WriteLine($"[RadioPlayerService] Successfully set album art from metadata");
                        }
                        else
                        {
                            Debug.WriteLine($"[RadioPlayerService] Failed to set album art from metadata, will try favicon");
                        }
                    }
                    else
                    {
                        thumbnailSet = true; // Already set, no need to update
                        TryEnqueueOnUi(() => updater.Update());
                    }
                }

                // If metadata album art failed or wasn't available, try station favicon
                if (!thumbnailSet && !string.IsNullOrWhiteSpace(_currentStationFaviconUrl))
                {
                    if (_currentStationFaviconUrl != _currentAlbumArtUrl)
                    {
                        Debug.WriteLine($"[RadioPlayerService] Attempting to set favicon as fallback: {_currentStationFaviconUrl}");
                        thumbnailSet = await SetAlbumArtAsync(updater, _currentStationFaviconUrl);

                        if (thumbnailSet)
                        {
                            _currentAlbumArtUrl = _currentStationFaviconUrl;
                            Debug.WriteLine($"[RadioPlayerService] Successfully set favicon as thumbnail");
                        }
                        else
                        {
                            Debug.WriteLine($"[RadioPlayerService] Failed to set favicon as thumbnail");
                            _currentAlbumArtUrl = null; // Reset so we can retry later
                        }
                    }
                    else
                    {
                        thumbnailSet = true; // Already set, no need to update
                        TryEnqueueOnUi(() => updater.Update());
                    }
                }

                // If both failed or weren't available, clear the thumbnail
                if (!thumbnailSet)
                {
                    TryEnqueueOnUi(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(_currentAlbumArtUrl))
                        {
                            _currentAlbumArtUrl = null;
                            updater.Thumbnail = null;
                            Debug.WriteLine("[RadioPlayerService] Cleared album art");
                        }
                        updater.Update();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Failed to update SystemMediaTransportControls: {ex.Message}");
        }
    }

    private async Task<bool> SetAlbumArtAsync(SystemMediaTransportControlsDisplayUpdater updater, string imageUrl)
    {
        try
        {
            Debug.WriteLine($"[RadioPlayerService] Downloading album art from: {imageUrl}");

            // Download the image
            byte[] imageData = await _httpClient.GetByteArrayAsync(imageUrl);
            Debug.WriteLine($"[RadioPlayerService] Downloaded {imageData.Length} bytes of album art");

            // Create a random access stream from the image data
            InMemoryRandomAccessStream stream = new();
            DataWriter writer = new(stream.GetOutputStreamAt(0));
            writer.WriteBytes(imageData);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            writer.Dispose();

            // Seek to the beginning of the stream
            stream.Seek(0);

            // Create a RandomAccessStreamReference from the stream
            RandomAccessStreamReference thumbnail = RandomAccessStreamReference.CreateFromStream(stream);

            // Set the thumbnail on the UI thread
            bool success = false;
            TryEnqueueOnUi(() =>
            {
                try
                {
                    updater.Thumbnail = thumbnail;
                    updater.Update();
                    Debug.WriteLine("[RadioPlayerService] Album art set successfully");
                    success = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RadioPlayerService] Failed to set album art thumbnail: {ex.Message}");
                }
                finally
                {
                    // Dispose the stream after setting the thumbnail
                    stream?.Dispose();
                }
            });
            return success;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Failed to download album art: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioPlayerService] Error setting album art: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        Debug.WriteLine("[RadioPlayerService] Dispose called");

        // Dispose the debounce timer
        _smtcUpdateTimer?.Dispose();
        _smtcUpdateTimer = null;

        // Dispose the internal state change timer
        _internalStateChangeTimer?.Dispose();
        _internalStateChangeTimer = null;

        _watchdog.Dispose();
        _metadataService.Dispose();
        _httpClient.Dispose();

        if (_player.Source is MediaSource media)
        {
            media.Reset();
            media.Dispose();
        }

        _player.Dispose();
        Debug.WriteLine("[RadioPlayerService] Disposed");
    }
}
