using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Helpers;
using Trdo.Models;
using Trdo.Services;
using Windows.System;

namespace Trdo.ViewModels;

public sealed partial class PlayerViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private readonly RadioStationService _stationService = RadioStationService.Instance;
    private readonly FavoritesService _favoritesService = FavoritesService.Instance;
    private string _watchdogStatus = string.Empty;
    private RadioStation? _selectedStation;
    private string? _lastError;
    private bool _isCurrentTrackFavorited;
    private bool _isRefreshingMetadata;
    private CancellationTokenSource? _stationTransitionCts;

    // Debounces persisting per-station volume changes so dragging the slider does
    // not rewrite stations.json on every tick.
    private readonly Timer _saveStationsTimer;
    private const int SaveStationsDebounceMs = 500;

    private static readonly Lazy<PlayerViewModel> _instance = new(() => new PlayerViewModel());
    public static PlayerViewModel Shared => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsCurrentTrackFavorited
    {
        get => _isCurrentTrackFavorited;
        private set
        {
            if (_isCurrentTrackFavorited == value) return;
            _isCurrentTrackFavorited = value;
            OnPropertyChanged();
        }
    }

    public PlayerViewModel()
    {
        Debug.WriteLine("=== PlayerViewModel Constructor START ===");

        _saveStationsTimer = new Timer(_ => FlushStationsSave(), null, Timeout.Infinite, Timeout.Infinite);

        _player.NextStationRequested += (_, _) => SelectNextStation();
        _player.PreviousStationRequested += (_, _) => SelectPreviousStation();

        // Failures raised by the player go straight to PlaybackErrorService, which decides
        // whether they are still worth showing. Recorded here only so LastError reflects them.
        _player.PlaybackFailed += (_, message) =>
        {
            Debug.WriteLine($"[PlayerViewModel] PlaybackFailed from service: {message}");
            _lastError = message;
        };

        _player.PlaybackStateChanged += (_, _) =>
        {
            Debug.WriteLine($"[PlayerViewModel] PlaybackStateChanged event fired. IsPlaying={IsPlaying}");
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPlaybackActive));
            OnPropertyChanged(nameof(CanRefreshMetadata));
            OnPropertyChanged(nameof(PlaybackButtonGlyph));
            OnPropertyChanged(nameof(PlaybackButtonText));
            OnPropertyChanged(nameof(MiniPlayerCloseButtonText));
            OnPropertyChanged(nameof(MiniPlayerCloseButtonVisibility));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonVisibility));
            OnPropertyChanged(nameof(MiniPlayerActiveContentVisibility));
            OnPropertyChanged(nameof(MiniPlayerIdleContentVisibility));
            OnPropertyChanged(nameof(CurrentTrackDisplay));
            OnPropertyChanged(nameof(CurrentTrackSupportingText));
            OnPropertyChanged(nameof(MiniPlayerPrimaryText));
            OnPropertyChanged(nameof(MiniPlayerSecondaryText));
            OnPropertyChanged(nameof(HasMiniPlayerSecondaryText));
        };
        _player.BufferingStateChanged += (_, _) =>
        {
            Debug.WriteLine($"[PlayerViewModel] BufferingStateChanged event fired. IsBuffering={IsBuffering}");        
            OnPropertyChanged(nameof(IsBuffering));
            OnPropertyChanged(nameof(IsPlaybackActive));
            OnPropertyChanged(nameof(CanRefreshMetadata));
            OnPropertyChanged(nameof(HasMiniPlayerSecondaryText));
            OnPropertyChanged(nameof(PlaybackButtonGlyph));
            OnPropertyChanged(nameof(PlaybackButtonText));
            OnPropertyChanged(nameof(MiniPlayerCloseButtonText));
            OnPropertyChanged(nameof(MiniPlayerCloseButtonVisibility));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonVisibility));
            OnPropertyChanged(nameof(MiniPlayerActiveContentVisibility));
            OnPropertyChanged(nameof(MiniPlayerIdleContentVisibility));
            OnPropertyChanged(nameof(CurrentTrackDisplay));
            OnPropertyChanged(nameof(CurrentTrackSupportingText));
            OnPropertyChanged(nameof(MiniPlayerPrimaryText));
            OnPropertyChanged(nameof(MiniPlayerSecondaryText));
            OnPropertyChanged(nameof(HasMiniPlayerSecondaryText));
        };

        _player.VolumeChanged += (_, _) =>
        {
            Debug.WriteLine($"[PlayerViewModel] VolumeChanged event fired. Volume={Volume}");
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumePercent));
        };

        // Subscribe to watchdog status changes
        _player.Watchdog.StreamStatusChanged += (_, args) =>
        {
            WatchdogStatus = $"{args.Status}: {args.Message}";
            Debug.WriteLine($"[PlayerViewModel] Watchdog status: {WatchdogStatus}");
        };

        // Subscribe to buffer level changes (for auto-buffer increase)
        _player.Watchdog.BufferLevelChanged += (_, newLevel) =>
        {
            Debug.WriteLine($"[PlayerViewModel] BufferLevelChanged event fired. NewLevel={newLevel}");
            OnPropertyChanged(nameof(BufferLevel));
            OnPropertyChanged(nameof(BufferLevelDescription));
        };

        // Subscribe to stream metadata changes
        _player.StreamMetadataChanged += (_, metadata) =>
        {
            Debug.WriteLine($"[PlayerViewModel] StreamMetadataChanged event fired. NowPlaying={metadata.DisplayText}");
            OnPropertyChanged(nameof(CurrentMetadata));
            OnPropertyChanged(nameof(NowPlaying));
            OnPropertyChanged(nameof(HasNowPlaying));
            OnPropertyChanged(nameof(MetadataArtistDisplay));
            OnPropertyChanged(nameof(MetadataTitleDisplay));
            OnPropertyChanged(nameof(CurrentAlbumArtImageSource));
            OnPropertyChanged(nameof(CurrentTrackDisplay));
            OnPropertyChanged(nameof(CurrentTrackSupportingText));
            OnPropertyChanged(nameof(MiniPlayerPrimaryText));
            OnPropertyChanged(nameof(MiniPlayerSecondaryText));
            OnPropertyChanged(nameof(HasMiniPlayerSecondaryText));
            OnPropertyChanged(nameof(ShowMiniPlayerSearchLinks));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonVisibility));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonText));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonGlyph));
            UpdateCurrentTrackFavoriteStatus();
        };

        _favoritesService.FavoritesChanged += (_, _) =>
        {
            UpdateCurrentTrackFavoriteStatus();
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonText));
            OnPropertyChanged(nameof(MiniPlayerFavoriteButtonGlyph));
        };
        SettingsService.MusicSearchServicesChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsSpotifyEnabled));
            OnPropertyChanged(nameof(IsDiscogsEnabled));
            OnPropertyChanged(nameof(IsAppleMusicEnabled));
            OnPropertyChanged(nameof(IsYouTubeMusicEnabled));
            OnPropertyChanged(nameof(HasEnabledMusicServices));
            OnPropertyChanged(nameof(ShowMiniPlayerSearchLinks));
        };

        // Load stations from settings
        Debug.WriteLine("[PlayerViewModel] Loading stations from settings...");
        List<RadioStation> loadedStations = _stationService.LoadStations();
        Debug.WriteLine($"[PlayerViewModel] Loaded {loadedStations.Count} stations");
        Stations = new ObservableCollection<RadioStation>(loadedStations);

        // Subscribe to collection changes to update CanPlay
        // We notify on all changes since CanPlay depends on Stations.Count > 0
        Stations.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanCycleStations));
            SyncStationCyclingAvailability();
        };

        // Load the previously selected station
        int selectedIndex = _stationService.LoadSelectedStationIndex();
        Debug.WriteLine($"[PlayerViewModel] Previously selected station index: {selectedIndex}");

        if (selectedIndex >= 0 && selectedIndex < Stations.Count)
        {
            _selectedStation = Stations[selectedIndex];
            Debug.WriteLine($"[PlayerViewModel] Restored selected station: {_selectedStation.Name} ({_selectedStation.StreamUrl})");
        }
        else if (Stations.Count > 0)
        {
            _selectedStation = Stations[0];
            Debug.WriteLine($"[PlayerViewModel] No valid saved index, selecting first station: {_selectedStation.Name} ({_selectedStation.StreamUrl})");
        }
        else
        {
            Debug.WriteLine("[PlayerViewModel] No stations available");
        }

        SyncStationCyclingAvailability();

        // Initialize with selected station's URL if available
        if (_selectedStation != null)
        {
            // Apply the restored station's saved volume and buffer override before
            // playback starts, so the first connection already uses them.
            _player.Volume = _selectedStation.Volume;
            _player.Watchdog.StationBufferLevelOverride = _selectedStation.BufferLevel;

            Debug.WriteLine($"[PlayerViewModel] Initializing stream with URL: {_selectedStation.StreamUrl}");
            InitializeStream(_selectedStation.StreamUrl);

            // Auto-play on startup if the setting is enabled
            if (SettingsService.AutoPlayOnStartup)
            {
                Debug.WriteLine("[PlayerViewModel] AutoPlayOnStartup is enabled, starting playback...");
                _player.Play();
            }
        }
        else
        {
            Debug.WriteLine("[PlayerViewModel] No selected station to initialize");
        }

        Debug.WriteLine("=== PlayerViewModel Constructor END ===");
        UpdateCurrentTrackFavoriteStatus();
    }

    public ObservableCollection<RadioStation> Stations { get; }

    public RadioStation? SelectedStation
    {
        get => _selectedStation;
        set
        {
            Debug.WriteLine($"=== SelectedStation SETTER START ===");
            Debug.WriteLine($"[PlayerViewModel] Current station: {(_selectedStation?.Name ?? "null")}");
            Debug.WriteLine($"[PlayerViewModel] New station: {(value?.Name ?? "null")}");

            if (value == _selectedStation)
            {
                Debug.WriteLine("[PlayerViewModel] Same station selected, no change needed");
                Debug.WriteLine($"=== SelectedStation SETTER END (no change) ===");
                return;
            }

            bool shouldResumePlayback = IsPlaying || IsBuffering;
            Debug.WriteLine($"[PlayerViewModel] Should resume playback after station change: {shouldResumePlayback}");

            CancelStationTransition();
            _selectedStation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(SelectedStationFallbackIconVisibility));
            OnPropertyChanged(nameof(SelectedStationFaviconImageSource));
            OnPropertyChanged(nameof(SelectedStationDisplayName));
            SyncStationCyclingAvailability();

            if (_selectedStation != null)
            {
                Debug.WriteLine($"[PlayerViewModel] New selected station: {_selectedStation.Name}");
                Debug.WriteLine($"[PlayerViewModel] Stream URL: {_selectedStation.StreamUrl}");

                // Save the selected station index
                int index = Stations.IndexOf(_selectedStation);
                Debug.WriteLine($"[PlayerViewModel] Station index in collection: {index}");
                if (index >= 0)
                {
                    _stationService.SaveSelectedStationIndex(index);
                    Debug.WriteLine($"[PlayerViewModel] Saved station index {index} to settings");
                }

                LogService.Info("PlayerViewModel",
                    $"Station selected: '{_selectedStation.Name}' ({LogService.Redact(_selectedStation.StreamUrl)}), volume={_selectedStation.Volume:0.00}");

                // Validate the URL
                if (!IsValidUrl(_selectedStation.StreamUrl))
                {
                    _lastError = $"Invalid stream URL for {_selectedStation.Name}";
                    LogService.Error("PlayerViewModel", _lastError);
                    Debug.WriteLine($"[PlayerViewModel] ERROR: {_lastError}");
                    PlaybackErrorService.Instance.Report(_lastError);
                    if (shouldResumePlayback)
                    {
                        Debug.WriteLine("[PlayerViewModel] Pausing player due to invalid URL");
                        _player.Pause();
                    }
                    Debug.WriteLine($"=== SelectedStation SETTER END (invalid URL) ===");
                    return;
                }

                try
                {
                    BeginStationTransition(_selectedStation, shouldResumePlayback);
                }
                catch (Exception ex)
                {
                    _lastError = $"Failed to switch to {_selectedStation.Name}: {ex.Message}";
                    Debug.WriteLine($"[PlayerViewModel] EXCEPTION: {_lastError}");
                    Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                    PlaybackErrorService.Instance.Report(_lastError);
                }
            }
            else
            {
                Debug.WriteLine("[PlayerViewModel] Selected station is null");
            }

            Debug.WriteLine($"=== SelectedStation SETTER END ===");
        }
    }

    public bool IsPlaying
    {
        get
        {
            bool isPlaying = _player.IsPlaying;
            Debug.WriteLine($"[PlayerViewModel] IsPlaying getter called, value: {isPlaying}");
            return isPlaying;
        }
    }

    public bool IsBuffering
    {
        get
        {
            bool isBuffering = _player.IsBuffering;
            Debug.WriteLine($"[PlayerViewModel] IsBuffering getter called, value: {isBuffering}");
            return isBuffering;
        }
    }

    public bool IsPlaybackActive => IsPlaying || IsBuffering;

    public string StreamUrl
    {
        get
        {
            string url = _player.StreamUrl ?? string.Empty;
            Debug.WriteLine($"[PlayerViewModel] StreamUrl getter called, value: {url}");
            return url;
        }
    }

    public bool WatchdogEnabled
    {
        get => _player.WatchdogEnabled;
        set
        {
            if (value == _player.WatchdogEnabled) return;
            Debug.WriteLine($"[PlayerViewModel] Setting WatchdogEnabled to {value}");
            _player.WatchdogEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether auto-buffer increase is enabled.
    /// When enabled, the buffer level automatically increases when stutter is detected.
    /// </summary>
    public bool AutoBufferIncreaseEnabled
    {
        get => _player.Watchdog.AutoBufferIncreaseEnabled;
        set
        {
            if (value == _player.Watchdog.AutoBufferIncreaseEnabled) return;
            Debug.WriteLine($"[PlayerViewModel] Setting AutoBufferIncreaseEnabled to {value}");
            _player.Watchdog.AutoBufferIncreaseEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the current buffer level (0-3).
    /// 0 = Default, 1 = Medium, 2 = Large, 3 = Extra Large
    /// </summary>
    public double BufferLevel
    {
        get => _player.Watchdog.BufferLevel;
        set
        {
            if (Math.Abs(value - _player.Watchdog.BufferLevel) < 0.0001) return;
            Debug.WriteLine($"[PlayerViewModel] Setting BufferLevel to {value}");
            _player.Watchdog.BufferLevel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BufferLevelDescription));
        }
    }

    /// <summary>
    /// Gets a human-readable description of the current buffer level.
    /// </summary>
    public string BufferLevelDescription => _player.Watchdog.BufferLevelDescription;

    /// <summary>
    /// Gets or sets the silence detection timeout in seconds.
    /// If audio is silent for longer than this, the stream will be restarted.
    /// </summary>
    public double SilenceTimeoutSeconds
    {
        get => _player.Watchdog.SilenceTimeoutSeconds;
        set
        {
            if (Math.Abs(value - _player.Watchdog.SilenceTimeoutSeconds) < 0.01) return;
            Debug.WriteLine($"[PlayerViewModel] Setting SilenceTimeoutSeconds to {value}");
            _player.Watchdog.SilenceTimeoutSeconds = value;
            OnPropertyChanged();
        }
    }

    public string WatchdogStatus
    {
        get => _watchdogStatus;
        private set
        {
            if (value == _watchdogStatus) return;
            _watchdogStatus = value;
            OnPropertyChanged();
        }
    }

    public string? LastError => _lastError;

    public bool CanPlay => Stations.Count > 0 && SelectedStation != null;

    public bool CanCycleStations => Stations.Count > 1;

    /// <summary>
    /// Gets the current stream metadata (now playing information).
    /// </summary>
    public StreamMetadata CurrentMetadata => _player.CurrentMetadata;

    /// <summary>
    /// Gets the current now playing text for display.
    /// </summary>
    public string NowPlaying => CurrentMetadata?.DisplayText ?? string.Empty;

    /// <summary>
    /// Indicates whether there is now playing information to display.
    /// </summary>
    public bool HasNowPlaying => CurrentMetadata?.HasMetadata ?? false;

    public string MetadataArtistDisplay => StreamMetadataFormatting.FormatArtist(CurrentMetadata);

    public string MetadataTitleDisplay => StreamMetadataFormatting.FormatTitle(CurrentMetadata);

    public bool IsRefreshingMetadata
    {
        get => _isRefreshingMetadata;
        private set
        {
            if (_isRefreshingMetadata == value)
            {
                return;
            }

            _isRefreshingMetadata = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefreshMetadata));
        }
    }

    public bool CanRefreshMetadata => IsPlaybackActive && !IsRefreshingMetadata;

    public async Task RefreshMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRefreshMetadata)
        {
            return;
        }

        try
        {
            IsRefreshingMetadata = true;
            await _player.RefreshMetadataAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayerViewModel] RefreshMetadataAsync failed: {ex.Message}");
        }
        finally
        {
            IsRefreshingMetadata = false;
        }
    }

    public string PlaybackButtonGlyph => IsBuffering
        ? "\uF16A"
        : IsPlaying
            ? "\uE769"
            : "\uE768";

    public string PlaybackButtonText => IsBuffering
        ? "Buffering"
        : IsPlaying
            ? "Pause"
            : "Play";

    public string MiniPlayerCloseButtonText => IsPlaying
        ? "Pause & close"
        : "";

    public Visibility MiniPlayerCloseButtonVisibility => IsPlaying
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string MiniPlayerFavoriteButtonText => IsCurrentTrackFavorited
        ? "Remove favorite"
        : "Favorite track";

    public string MiniPlayerFavoriteButtonGlyph => IsCurrentTrackFavorited
        ? "\uE735"
        : "\uE734";

    public Visibility MiniPlayerFavoriteButtonVisibility => IsPlaybackActive && CurrentMetadata?.HasMetadata == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MiniPlayerActiveContentVisibility => IsPlaybackActive
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MiniPlayerIdleContentVisibility => IsPlaybackActive
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string MiniPlayerPrimaryText => MetadataTitleDisplay;

    public string MiniPlayerSecondaryText => MetadataArtistDisplay;

    public bool HasMiniPlayerSecondaryText => IsPlaybackActive;

    public string CurrentTrackDisplay => MetadataTitleDisplay;

    public string CurrentTrackSupportingText => MetadataArtistDisplay;

    public ImageSource? CurrentAlbumArtImageSource => CreateImageSource(CurrentMetadata?.AlbumArtUrl);

    public ImageSource? SelectedStationFaviconImageSource => CreateImageSource(SelectedStation?.FaviconUrl);

    public Visibility SelectedStationFallbackIconVisibility => SelectedStationFaviconImageSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SelectedStationDisplayName => SelectedStation?.Name ?? "No station selected";

    public bool IsSpotifyEnabled => SettingsService.IsSpotifyEnabled;

    public bool IsDiscogsEnabled => SettingsService.IsDiscogsEnabled;

    public bool IsAppleMusicEnabled => SettingsService.IsAppleMusicEnabled;

    public bool IsYouTubeMusicEnabled => SettingsService.IsYouTubeMusicEnabled;

    public bool HasEnabledMusicServices =>
        IsSpotifyEnabled ||
        IsDiscogsEnabled ||
        IsAppleMusicEnabled ||
        IsYouTubeMusicEnabled;

    public bool ShowMiniPlayerSearchLinks => HasNowPlaying && HasEnabledMusicServices;

    public double Volume
    {
        get => _player.Volume;
        set
        {
            Debug.WriteLine($"[PlayerViewModel] Setting Volume to {value}");
            _player.Volume = value;
            OnPropertyChanged();

            // Remember the level on the current station so it follows the station (#16).
            if (_selectedStation is not null)
            {
                double clamped = _player.Volume;
                if (_selectedStation.Volume != clamped)
                {
                    _selectedStation.Volume = clamped;
                    ScheduleStationsSave();
                }
            }
        }
    }

    /// <summary>
    /// Volume expressed as a percentage of the stream volume (100 = full stream
    /// volume, up to 200 for amplification). Backed by <see cref="Volume"/>.
    /// </summary>
    public double VolumePercent
    {
        get => _player.Volume * 100;
        set => Volume = value / 100;
    }

    /// <summary>
    /// Requests a debounced save of the station list, coalescing rapid volume
    /// changes (e.g. dragging the slider) into a single write.
    /// </summary>
    private void ScheduleStationsSave() =>
        _saveStationsTimer.Change(SaveStationsDebounceMs, Timeout.Infinite);

    /// <summary>
    /// Persists the station list immediately and cancels any pending debounced save.
    /// </summary>
    public void FlushStationsSave()
    {
        _saveStationsTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stationService.SaveStations(Stations);
    }

    public void Toggle()
    {
        Debug.WriteLine("=== Toggle START ===");
        Debug.WriteLine($"[PlayerViewModel] Current IsPlaying: {IsPlaying}");
        Debug.WriteLine($"[PlayerViewModel] Selected station: {(_selectedStation?.Name ?? "null")}");
        Debug.WriteLine($"[PlayerViewModel] Current stream URL in player: {_player.StreamUrl ?? "null"}");

        try
        {
            CancelStationTransition();

            // This attempt supersedes the last one, so an unseen error from it is now
            // about history the user has already responded to by pressing play.
            PlaybackErrorService.Instance.ClearPendingError();

            _player.TogglePlayPause();
            Debug.WriteLine($"[PlayerViewModel] TogglePlayPause called successfully. New IsPlaying: {IsPlaying}");
            _lastError = null;
        }
        catch (Exception ex)
        {
            string stationName = _selectedStation?.Name ?? "Unknown";
            _lastError = $"Failed to play {stationName}: {ex.Message}";
            Debug.WriteLine($"[PlayerViewModel] EXCEPTION in Toggle: {_lastError}");
            Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
            PlaybackErrorService.Instance.Report(_lastError);
        }

        Debug.WriteLine("=== Toggle END ===");
    }

    public void Pause()
    {
        Debug.WriteLine("=== Pause START ===");
        Debug.WriteLine($"[PlayerViewModel] Current IsPlaying: {IsPlaying}");

        if (!IsPlaying && !IsBuffering)
        {
            Debug.WriteLine("[PlayerViewModel] Pause skipped because playback is already idle");
            Debug.WriteLine("=== Pause END (idle) ===");
            return;
        }

        try
        {
            CancelStationTransition();
            _player.Pause();
            _lastError = null;
        }
        catch (Exception ex)
        {
            string stationName = _selectedStation?.Name ?? "Unknown";
            _lastError = $"Failed to pause {stationName}: {ex.Message}";
            Debug.WriteLine($"[PlayerViewModel] EXCEPTION in Pause: {_lastError}");
            Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
            PlaybackErrorService.Instance.Report(_lastError);
        }

        Debug.WriteLine("=== Pause END ===");
    }

    public void ToggleCurrentTrackFavorite()
    {
        if (CurrentMetadata?.HasMetadata != true)
            return;

        string stationName = SelectedStation?.Name ?? "Unknown Station";
        IsCurrentTrackFavorited = _favoritesService.ToggleFavorite(CurrentMetadata, stationName);
    }

    public async Task SearchOnDiscogs()
    {
        if (!HasNowPlaying)
            return;

        string url = $"https://www.discogs.com/search?q={Uri.EscapeDataString(NowPlaying)}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    public async Task SearchOnSpotify()
    {
        if (!HasNowPlaying)
            return;

        string query = Uri.EscapeDataString(NowPlaying);
        string spotifyAppUri = $"spotify:search:{query}";

        try
        {
            bool success = await Launcher.LaunchUriAsync(new Uri(spotifyAppUri));

            if (!success)
            {
                await Launcher.LaunchUriAsync(new Uri($"https://open.spotify.com/search/{query}"));
            }
        }
        catch
        {
            await Launcher.LaunchUriAsync(new Uri($"https://open.spotify.com/search/{query}"));
        }
    }

    public async Task SearchOnAppleMusic()
    {
        if (!HasNowPlaying)
            return;

        await MusicSearchLinkService.LaunchAppleMusicWebSearchAsync(NowPlaying);
    }

    public async Task SearchOnYouTubeMusic()
    {
        if (!HasNowPlaying)
            return;

        string query = Uri.EscapeDataString(NowPlaying);
        await Launcher.LaunchUriAsync(new Uri($"https://music.youtube.com/search?q={query}"));
    }

    public void RestoreSelectedStationPlaybackTarget()
    {
        Debug.WriteLine("=== RestoreSelectedStationPlaybackTarget START ===");

        try
        {
            if (_selectedStation is null)
            {
                CancelStationTransition();
                _player.ClearPlaybackTarget();
            }
            else if (!IsValidUrl(_selectedStation.StreamUrl))
            {
                throw new InvalidOperationException($"Invalid stream URL for {_selectedStation.Name}");
            }
            else
            {
                BeginStationTransition(_selectedStation, playAfterSwitch: false);
            }

            _lastError = null;
        }
        catch (Exception ex)
        {
            string stationName = _selectedStation?.Name ?? "Unknown";
            _lastError = $"Failed to restore {stationName}: {ex.Message}";
            Debug.WriteLine($"[PlayerViewModel] EXCEPTION in RestoreSelectedStationPlaybackTarget: {_lastError}");
            Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
            PlaybackErrorService.Instance.Report(_lastError);
        }

        Debug.WriteLine("=== RestoreSelectedStationPlaybackTarget END ===");
    }

    public bool SelectNextStation()
    {
        return TryCycleStation(1, "next");
    }

    public bool SelectPreviousStation()
    {
        return TryCycleStation(-1, "previous");
    }

    private void UpdateCurrentTrackFavoriteStatus()
    {
        IsCurrentTrackFavorited = _favoritesService.IsFavorited(CurrentMetadata);
    }

    /// <summary>
    /// Add a new station and save to settings
    /// </summary>
    public void AddStation(RadioStation station)
    {
        if (station is null)
            return;

        Debug.WriteLine($"[PlayerViewModel] Adding station: {station.Name} ({station.StreamUrl})");
        Stations.Add(station);
        _stationService.SaveStations(Stations);

        // If this is the first station, select it automatically
        if (Stations.Count == 1)
        {
            Debug.WriteLine("[PlayerViewModel] First station added, selecting automatically");
            SelectedStation = station;
        }
    }

    public async Task VisitWebsite(RadioStation station)
    {
        Debug.WriteLine($"[PlayerViewModel] Visiting station website: {station.Name} ({station.StreamUrl})");

        if (string.IsNullOrWhiteSpace(station.Homepage) || !IsValidUrl(station.Homepage))
            return;

        await Launcher.LaunchUriAsync(new Uri(station.Homepage));
    }

    /// <summary>
    /// Remove a station and save to settings
    /// </summary>
    public void RemoveStation(RadioStation station)
    {
        if (station == null) return;

        Debug.WriteLine($"[PlayerViewModel] Removing station: {station.Name} ({station.StreamUrl})");

        // If removing the selected station, select another one first
        if (station == _selectedStation)
        {
            Debug.WriteLine("[PlayerViewModel] Removing currently selected station");
            if (Stations.Count > 1)
            {
                int currentIndex = Stations.IndexOf(station);
                int newIndex = currentIndex > 0 ? currentIndex - 1 : 1;
                Debug.WriteLine($"[PlayerViewModel] Selecting station at index {newIndex}");
                SelectedStation = Stations[newIndex];
            }
            else
            {
                // Last station - stop playback and clear selection
                Debug.WriteLine("[PlayerViewModel] Removing last station, stopping playback");
                if (IsPlaying || IsBuffering)
                {
                    _player.Pause();
                }
                _selectedStation = null;
                OnPropertyChanged(nameof(SelectedStation));
                OnPropertyChanged(nameof(CanPlay));
            }
        }

        Stations.Remove(station);
        _stationService.SaveStations(Stations);
        Debug.WriteLine($"[PlayerViewModel] Station removed, {Stations.Count} stations remaining");
    }

    /// <summary>
    /// Save the current stations list to settings (used when editing stations)
    /// </summary>
    public void SaveStations()
    {
        Debug.WriteLine("[PlayerViewModel] SaveStations called");
        // Cancel any pending debounced volume save; this write covers it.
        _saveStationsTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stationService.SaveStations(Stations);

        // If the current station was edited, reinitialize the stream
        if (_selectedStation != null && IsValidUrl(_selectedStation.StreamUrl))
        {
            Debug.WriteLine($"[PlayerViewModel] Reinitializing stream after save: {_selectedStation.StreamUrl}");
            try
            {
                bool wasPlaybackActive = IsPlaying || IsBuffering;
                BeginStationTransition(_selectedStation, wasPlaybackActive);
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to update stream: {ex.Message}";
                Debug.WriteLine($"[PlayerViewModel] EXCEPTION in SaveStations: {_lastError}");
                Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                PlaybackErrorService.Instance.Report(_lastError);
            }
        }
    }

    /// <summary>
    /// Update the saved index of the currently selected station (used after reordering)
    /// </summary>
    public void UpdateSelectedStationIndex()
    {
        if (_selectedStation != null)
        {
            int index = Stations.IndexOf(_selectedStation);
            if (index >= 0)
            {
                Debug.WriteLine($"[PlayerViewModel] Updating selected station index to {index}");
                _stationService.SaveSelectedStationIndex(index);
            }
        }
    }

    private void InitializeStream(string streamUrl)
    {
        Debug.WriteLine($"[PlayerViewModel] InitializeStream called with URL: {streamUrl}");
        if (IsValidUrl(streamUrl))
        {
            try
            {
                _player.Initialize(streamUrl);
                Debug.WriteLine($"[PlayerViewModel] Player initialized with URL: {streamUrl}");

                // Set the station name if we have one
                if (_selectedStation != null)
                {
                    Debug.WriteLine($"[PlayerViewModel] Setting station name during initialization: {_selectedStation.Name}");
                    _player.SetStationName(_selectedStation.Name);

                    Debug.WriteLine($"[PlayerViewModel] Setting station favicon during initialization: {_selectedStation.FaviconUrl}");
                    _player.SetStationFavicon(_selectedStation.FaviconUrl);
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to initialize stream: {ex.Message}";
                Debug.WriteLine($"[PlayerViewModel] EXCEPTION in InitializeStream: {_lastError}");
                Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                PlaybackErrorService.Instance.Report(_lastError);
            }
        }
        else
        {
            Debug.WriteLine($"[PlayerViewModel] Invalid URL, skipping initialization: {streamUrl}");
        }
    }

    private bool TryCycleStation(int direction, string directionName)
    {
        Debug.WriteLine($"=== TryCycleStation START ({directionName}) ===");

        if (Stations.Count == 0)
        {
            Debug.WriteLine("[PlayerViewModel] No stations available to cycle");
            Debug.WriteLine($"=== TryCycleStation END ({directionName}, no stations) ===");
            return false;
        }

        if (Stations.Count == 1)
        {
            Debug.WriteLine("[PlayerViewModel] Station cycling skipped because only one station is available");
            Debug.WriteLine($"=== TryCycleStation END ({directionName}, single station) ===");
            return false;
        }

        int currentIndex = _selectedStation is null ? -1 : Stations.IndexOf(_selectedStation);
        int newIndex = currentIndex >= 0
            ? (currentIndex + direction + Stations.Count) % Stations.Count
            : direction > 0
                ? 0
                : Stations.Count - 1;

        Debug.WriteLine($"[PlayerViewModel] Cycling {directionName} from index {currentIndex} to {newIndex}");
        SelectedStation = Stations[newIndex];
        Debug.WriteLine($"=== TryCycleStation END ({directionName}) ===");
        return true;
    }

    private void SyncStationCyclingAvailability()
    {
        Debug.WriteLine($"[PlayerViewModel] Syncing station cycling availability: {CanCycleStations}");
        _player.SetStationCyclingEnabled(CanCycleStations);
    }

    private void BeginStationTransition(RadioStation station, bool playAfterSwitch)
    {
        // Must land before the transition starts: the buffer level is read when the
        // stream connects, so setting it afterwards would not take effect until the
        // next switch. Also covers SaveStations, which re-enters here after an edit.
        _player.Watchdog.StationBufferLevelOverride = station.BufferLevel;

        CancelStationTransition();
        CancellationTokenSource transitionCts = new();
        _stationTransitionCts = transitionCts;
        _ = TransitionToStationAsync(station, playAfterSwitch, transitionCts);
    }

    private async Task TransitionToStationAsync(
        RadioStation station,
        bool playAfterSwitch,
        CancellationTokenSource transitionCts)
    {
        try
        {
            await _player.TransitionToStationAsync(
                station.StreamUrl,
                station.Name,
                station.FaviconUrl,
                station.Volume,
                playAfterSwitch,
                transitionCts.Token);

            if (ReferenceEquals(_selectedStation, station))
            {
                _lastError = null;
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumePercent));
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[PlayerViewModel] Station transition cancelled: {station.Name}");
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_selectedStation, station))
            {
                _lastError = $"Failed to switch to {station.Name}: {ex.Message}";
                Debug.WriteLine($"[PlayerViewModel] EXCEPTION: {_lastError}");
                Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                PlaybackErrorService.Instance.Report(_lastError);
            }
        }
        finally
        {
            if (ReferenceEquals(_stationTransitionCts, transitionCts))
            {
                _stationTransitionCts = null;
            }

            transitionCts.Dispose();
        }
    }

    private void CancelStationTransition()
    {
        CancellationTokenSource? transitionCts = _stationTransitionCts;
        _stationTransitionCts = null;
        transitionCts?.Cancel();
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static ImageSource? CreateImageSource(string? url)
    {
        if (!IsValidUrl(url))
        {
            return null;
        }

        return new BitmapImage(new Uri(url!, UriKind.Absolute));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        Debug.WriteLine($"[PlayerViewModel] PropertyChanged: {name}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
