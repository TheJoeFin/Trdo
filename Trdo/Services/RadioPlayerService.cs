using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Trdo.Services;

public sealed partial class RadioPlayerService : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly DispatcherQueue _uiQueue;
    private readonly StreamWatchdogService _watchdog;
    private double _volume = 0.5;
    private const string VolumeKey = "RadioVolume";
    private const string WatchdogEnabledKey = "WatchdogEnabled";
    private string? _streamUrl;
    private bool _isInternalStateChange;

    public static RadioPlayerService Instance { get; } = new();

    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<double>? VolumeChanged;

    public bool IsPlaying
    {
        get
        {
            bool isPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            Debug.WriteLine($"[RadioPlayerService] IsPlaying getter: {isPlaying}, PlaybackState: {_player.PlaybackSession.PlaybackState}");
            return isPlaying;
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

        _player.PlaybackSession.PlaybackStateChanged += (_, _) =>
        {
            bool isPlaying;
            MediaPlaybackState currentState;
            try
            {
                currentState = _player.PlaybackSession.PlaybackState;
                isPlaying = currentState == MediaPlaybackState.Playing;
                Debug.WriteLine($"[RadioPlayerService] PlaybackStateChanged event: IsPlaying={isPlaying}, State={currentState}, IsInternalChange={_isInternalStateChange}");
                
                // If state change was not initiated internally (e.g., from hardware buttons),
                // notify the watchdog of user intention
                if (!_isInternalStateChange)
                {
                    Debug.WriteLine("[RadioPlayerService] External state change detected (likely hardware button)");
                    if (currentState == MediaPlaybackState.Playing)
                    {
                        _watchdog.NotifyUserIntentionToPlay();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to play (hardware button)");
                    }
                    else if (currentState == MediaPlaybackState.Paused)
                    {
                        // Only notify pause intent if explicitly paused (not buffering, opening, or other states)
                        _watchdog.NotifyUserIntentionToPause();
                        Debug.WriteLine("[RadioPlayerService] Notified watchdog of user intention to pause (hardware button)");
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
    /// Start playback of the current stream
    /// </summary>
    public void Play()
    {
        Debug.WriteLine($"=== Play START ===");
        Debug.WriteLine($"[RadioPlayerService] Play called");
        Debug.WriteLine($"[RadioPlayerService] Current stream URL: {_streamUrl}");
        Debug.WriteLine($"[RadioPlayerService] Current IsPlaying: {_player.PlaybackSession.PlaybackState}");
        Debug.WriteLine($"[RadioPlayerService] Player.Source is null: {_player.Source == null}");

        if (string.IsNullOrWhiteSpace(_streamUrl))
        {
            Debug.WriteLine("[RadioPlayerService] ERROR: No stream URL set");
            throw new InvalidOperationException("No stream URL set. Call SetStreamUrl first.");
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