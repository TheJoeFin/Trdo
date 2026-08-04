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

    /// <summary>The arrangement: top-level stations, folders and dividers, in display order.</summary>
    private readonly List<object> _topLevelNodes;

    private StationSortMode _sortMode = SettingsService.StationSortMode;

    /// <summary>
    /// Set while <see cref="RebuildDisplayRows"/> is editing <see cref="DisplayRows"/>, and
    /// while a drag reorder is in flight. The list control performs a reorder by mutating the
    /// bound collection itself, so anything that reacts to <c>CollectionChanged</c> by
    /// rebuilding would corrupt the operation half-way through.
    /// </summary>
    private bool _isRebuildingDisplayRows;

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
            OnPropertyChanged(nameof(StationCount));
            SyncStationCyclingAvailability();
        };

        // Rebuild the arrangement from the saved layout. With no layout file - which is every
        // user who has not made a folder - this is just the stations in file order, so the
        // list looks exactly as it always has.
        _topLevelNodes = StationLayoutPolicy.Reconcile(Stations, _stationService.LoadLayout());
        RebuildDisplayRows();

        // Load the previously selected station, by id where one was saved and by the legacy
        // index on the first run after upgrading.
        _selectedStation = StationSelectionPolicy.Resolve(
            Stations,
            _stationService.LoadSelectedStationId(),
            _stationService.LoadSelectedStationIndex());

        if (_selectedStation is not null)
        {
            _selectedStation.IsSelectedStation = true;
            Debug.WriteLine($"[PlayerViewModel] Restored selected station: {_selectedStation.Name} ({_selectedStation.StreamUrl})");
            // Complete the migration: whatever it was resolved from, it is stored by id now.
            _stationService.SaveSelectedStation(_selectedStation.Id, Stations.IndexOf(_selectedStation));
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

    /// <summary>
    /// Every station the user has, flat, in the order they are written to disk. This is the
    /// authority on <em>which</em> stations exist; <see cref="TopLevelNodes"/> is the
    /// authority on how they are arranged.
    /// </summary>
    public ObservableCollection<RadioStation> Stations { get; }

    /// <summary>
    /// The arrangement: stations, folders and dividers at the top level, in display order.
    /// </summary>
    public IReadOnlyList<object> TopLevelNodes => _topLevelNodes;

    /// <summary>
    /// The rows the list control draws, holding the model objects themselves rather than
    /// wrappers.
    /// <para>
    /// Using the models directly is what keeps identity stable across a rebuild. A wrapper
    /// would be a new object every time a folder was expanded, so the list would lose its
    /// selection, raise <c>SelectionChanged</c>, and - through the
    /// <see cref="SelectedStation"/> setter - restart playback every time the user toggled a
    /// folder.
    /// </para>
    /// </summary>
    public ObservableCollection<object> DisplayRows { get; } = [];

    /// <summary>
    /// The folders that currently exist, for the "move to group" menu.
    /// </summary>
    public IReadOnlyList<StationGroup> Groups
    {
        get
        {
            List<StationGroup> groups = [];
            foreach (object node in _topLevelNodes)
            {
                if (node is StationGroup group)
                    groups.Add(group);
            }
            return groups;
        }
    }

    /// <summary>
    /// The stations that are currently visible, in the order they appear on screen.
    /// <para>
    /// Not the same as <see cref="Stations"/> once a view sort is active or a folder is
    /// collapsed. Anything that means "the next station" or "the one beside this one" has to
    /// use this, or it will pick a station the user cannot see.
    /// </para>
    /// </summary>
    public IReadOnlyList<RadioStation> DisplayOrderedStations
    {
        get
        {
            List<RadioStation> stations = [];
            foreach (object row in DisplayRows)
            {
                if (row is RadioStation station)
                    stations.Add(station);
            }
            return stations;
        }
    }

    /// <summary>
    /// How many stations there are. Exists because <c>x:Bind</c> to
    /// <c>Stations.Count</c> does not re-evaluate when the collection changes, which left the
    /// empty state stuck once it had been shown.
    /// </summary>
    public int StationCount => Stations.Count;

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
            RadioStation? previous = _selectedStation;
            _selectedStation = value;

            // Drives the row highlight. Kept on the model rather than resolved by the page,
            // so it survives virtualisation, collapsing and sorting without the page having
            // to hunt for containers.
            if (previous is not null)
                previous.IsSelectedStation = false;
            if (_selectedStation is not null)
                _selectedStation.IsSelectedStation = true;

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

                // Save which station is selected
                UpdateSelectedStationId();

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

        if (string.IsNullOrWhiteSpace(station.Id))
            station.Id = StationIdentityPolicy.NewId();
        station.DateAdded ??= DateTimeOffset.UtcNow;

        Stations.Add(station);

        // New stations land at the top level, not inside whichever folder happens to be open.
        // Putting them somewhere the user has to go looking is worse than putting them
        // somewhere obvious.
        _topLevelNodes.Add(station);
        RebuildDisplayRows();
        PersistStationList();

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
                // Pick the neighbour the user can actually see: the visible order differs from
                // the stored one as soon as a folder is collapsed or a view sort is active.
                IReadOnlyList<RadioStation> visible = DisplayOrderedStations;
                int currentIndex = -1;
                for (int i = 0; i < visible.Count; i++)
                {
                    if (ReferenceEquals(visible[i], station))
                    {
                        currentIndex = i;
                        break;
                    }
                }
                RadioStation? replacement = currentIndex switch
                {
                    > 0 => visible[currentIndex - 1],
                    0 when visible.Count > 1 => visible[1],
                    // The station being removed is inside a collapsed folder, so it is not on
                    // screen and there is no neighbour to speak of.
                    _ => Stations[0] == station && Stations.Count > 1 ? Stations[1] : Stations[0]
                };

                Debug.WriteLine($"[PlayerViewModel] Selecting '{replacement?.Name}' after removal");
                SelectedStation = replacement;
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
        RemoveFromTree(station);
        RebuildDisplayRows();
        PersistStationList();
        Debug.WriteLine($"[PlayerViewModel] Station removed, {Stations.Count} stations remaining");
    }

    /// <summary>
    /// Persist the station list without touching playback.
    /// <para>
    /// Use this for anything that changes <em>where</em> a station sits rather than
    /// <em>what it points at</em> - reordering, grouping, collapsing a folder. Those
    /// operations have no business restarting the stream, which is what
    /// <see cref="SaveStations"/> does.
    /// </para>
    /// </summary>
    public void PersistStationList()
    {
        // Cancel any pending debounced volume save; this write covers it.
        _saveStationsTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Layout first: see RadioStationService.SaveLayout for why the order matters.
        _stationService.SaveLayout(StationLayoutPolicy.ToDocument(_topLevelNodes));
        _stationService.SaveStations(Stations);
    }

    /// <summary>
    /// Recomputes the visible rows from the arrangement and the current sort.
    /// <para>
    /// Applies the result to <see cref="DisplayRows"/> in place rather than clearing and
    /// refilling it. Clearing would drop the list's selection, flash the list, throw away the
    /// scroll position and destroy container reuse - all of it visible, every time a folder
    /// is expanded.
    /// </para>
    /// <para>
    /// Never persists anything. Expanding a folder does persist (the state is remembered), but
    /// that is the caller's decision; changing the sort must not write, which is what makes a
    /// view sort non-destructive.
    /// </para>
    /// </summary>
    public void RebuildDisplayRows()
    {
        if (_isRebuildingDisplayRows)
            return;

        _isRebuildingDisplayRows = true;
        try
        {
            List<object> target = StationLayoutPolicy.Flatten(_topLevelNodes, _sortMode);

            for (int i = 0; i < target.Count; i++)
            {
                object wanted = target[i];

                if (i >= DisplayRows.Count)
                {
                    DisplayRows.Add(wanted);
                    continue;
                }

                if (ReferenceEquals(DisplayRows[i], wanted))
                    continue;

                int existing = IndexOfReference(DisplayRows, wanted, i + 1);
                if (existing >= 0)
                    DisplayRows.Move(existing, i);
                else
                    DisplayRows.Insert(i, wanted);
            }

            while (DisplayRows.Count > target.Count)
                DisplayRows.RemoveAt(DisplayRows.Count - 1);
        }
        finally
        {
            _isRebuildingDisplayRows = false;
        }

        OnPropertyChanged(nameof(DisplayOrderedStations));
    }

    /// <summary>
    /// Re-orders <see cref="Stations"/> to match the arrangement, depth-first.
    /// <para>
    /// Keeps the saved file in the order the user sees, so a build that ignores the layout
    /// still shows the stations grouped together the way they arranged them, just without the
    /// folder headers.
    /// </para>
    /// </summary>
    public void SyncStationsFromTree()
    {
        List<RadioStation> ordered = StationLayoutPolicy.CollectStations(_topLevelNodes);

        // Anything the tree does not account for stays where it is rather than disappearing.
        foreach (RadioStation station in Stations)
        {
            if (IndexOfReference(ordered, station, 0) < 0)
                ordered.Add(station);
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            int existing = IndexOfReference(Stations, ordered[i], i);
            if (existing > i)
                Stations.Move(existing, i);
        }
    }

    /// <summary>
    /// How the list is currently ordered on screen.
    /// <para>
    /// Changing this re-renders and nothing else. It deliberately does not persist the station
    /// list: that is the whole basis of the guarantee that a view sort is reversible, so
    /// switching back to <see cref="StationSortMode.Manual"/> returns the exact arrangement the
    /// user built.
    /// </para>
    /// </summary>
    public StationSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (value == _sortMode) return;
            _sortMode = value;
            SettingsService.StationSortMode = value;
            RebuildDisplayRows();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsViewSorted));
            OnPropertyChanged(nameof(SortHintText));
        }
    }

    /// <summary>True when a sort other than the user's own order is in effect.</summary>
    public bool IsViewSorted => _sortMode != StationSortMode.Manual;

    /// <summary>The one-line explanation shown above the list while a view sort is active.</summary>
    public string SortHintText => StationSortPolicy.HintText(_sortMode);

    /// <summary>
    /// Creates a folder and puts it at the end of the list.
    /// </summary>
    /// <param name="insertBefore">
    /// An existing row to place the folder above, or null to append. Inserting where the user
    /// is looking beats making them scroll to the bottom and drag it back up.
    /// </param>
    public StationGroup CreateGroup(string name, object? insertBefore = null)
    {
        StationGroup group = new()
        {
            Id = StationIdentityPolicy.NewId(),
            Name = string.IsNullOrWhiteSpace(name) ? "New group" : name.Trim()
        };

        InsertTopLevel(group, insertBefore);
        return group;
    }

    /// <summary>
    /// Creates a divider. Unlike a folder this can go inside one, so it is inserted next to
    /// the row it was created from rather than always at the top level.
    /// </summary>
    public StationDivider CreateDivider(string? label = null, object? insertBefore = null)
    {
        StationDivider divider = new()
        {
            Id = StationIdentityPolicy.NewId(),
            Label = label
        };

        if (insertBefore is not null && FindParentGroup(insertBefore) is StationGroup parent)
        {
            parent.Children.Insert(IndexOfReference(parent.Children, insertBefore, 0), divider);
            parent.NotifyChildrenChanged();
            RebuildDisplayRows();
            PersistStationList();
        }
        else
        {
            InsertTopLevel(divider, insertBefore);
        }

        return divider;
    }

    /// <summary>
    /// Moves a station into a folder, or out to the top level when <paramref name="group"/> is
    /// null.
    /// <para>
    /// This is the unambiguous counterpart to dragging. A row dropped just below a folder's
    /// last item reads as being inside it - there is no way to tell that apart from "just
    /// after it" - so getting a station back out needs a command that cannot be misread. The
    /// station lands immediately after the folder it left, where the user was already looking.
    /// </para>
    /// </summary>
    public void MoveStationToGroup(RadioStation station, StationGroup? group)
    {
        if (station is null)
            return;

        StationGroup? currentParent = FindParentGroup(station);
        if (ReferenceEquals(currentParent, group))
            return;

        if (currentParent is not null)
        {
            currentParent.Children.Remove(station);
            currentParent.NotifyChildrenChanged();
        }
        else
        {
            _topLevelNodes.Remove(station);
        }

        if (group is not null)
        {
            group.Children.Add(station);
            group.NotifyChildrenChanged();
            station.GroupId = group.Id;
        }
        else
        {
            station.GroupId = null;
            int afterFormerGroup = currentParent is null
                ? _topLevelNodes.Count
                : IndexOfReference(_topLevelNodes, currentParent, 0) + 1;
            _topLevelNodes.Insert(Math.Clamp(afterFormerGroup, 0, _topLevelNodes.Count), station);
        }

        SyncStationsFromTree();
        RebuildDisplayRows();
        PersistStationList();
    }

    /// <summary>
    /// Removes a folder. Its contents move up to the top level at the folder's old position,
    /// in order, so nothing is lost and no confirmation is needed - exactly the reasoning that
    /// makes removing a single station immediate.
    /// </summary>
    public void DeleteGroup(StationGroup group)
    {
        if (group is null)
            return;

        int position = IndexOfReference(_topLevelNodes, group, 0);
        if (position < 0)
            return;

        _topLevelNodes.RemoveAt(position);
        for (int i = 0; i < group.Children.Count; i++)
        {
            object child = group.Children[i];
            SetNodeGroupId(child, null);
            _topLevelNodes.Insert(position + i, child);
        }
        group.Children.Clear();

        SyncStationsFromTree();
        RebuildDisplayRows();
        PersistStationList();
    }

    /// <summary>
    /// Removes a folder and every station in it. Destructive and unrecoverable, so callers are
    /// expected to confirm first.
    /// </summary>
    public void DeleteGroupAndStations(StationGroup group)
    {
        if (group is null)
            return;

        List<RadioStation> doomed = [];
        foreach (object child in group.Children)
        {
            if (child is RadioStation station)
                doomed.Add(station);
        }

        // Move the selection off anything about to disappear before removing it, so playback
        // lands somewhere deliberate rather than being torn out from underneath.
        if (_selectedStation is not null && doomed.Contains(_selectedStation))
        {
            RadioStation? survivor = null;
            foreach (RadioStation candidate in Stations)
            {
                if (!doomed.Contains(candidate))
                {
                    survivor = candidate;
                    break;
                }
            }
            SelectedStation = survivor;
        }

        foreach (RadioStation station in doomed)
            Stations.Remove(station);

        group.Children.Clear();
        _topLevelNodes.Remove(group);

        SyncStationsFromTree();
        RebuildDisplayRows();
        PersistStationList();
    }

    /// <summary>Removes a divider. Nothing else moves.</summary>
    public void DeleteDivider(StationDivider divider)
    {
        if (divider is null)
            return;

        RemoveFromTree(divider);
        RebuildDisplayRows();
        PersistStationList();
    }

    /// <summary>
    /// Persists a rename or a label edit. The name itself is already set on the model, which
    /// the row is bound to; this just writes it out.
    /// </summary>
    public void CommitLayoutEdit() => PersistStationList();

    /// <summary>The folder an item sits in, or null when it is at the top level.</summary>
    public StationGroup? FindParentGroup(object item)
    {
        foreach (object node in _topLevelNodes)
        {
            if (node is StationGroup group && IndexOfReference(group.Children, item, 0) >= 0)
                return group;
        }
        return null;
    }

    private void InsertTopLevel(object node, object? insertBefore)
    {
        int position = _topLevelNodes.Count;
        if (insertBefore is not null)
        {
            // The anchor may be inside a folder, in which case the new row goes above that
            // whole folder rather than in the middle of it.
            object anchor = FindParentGroup(insertBefore) as object ?? insertBefore;
            int anchorIndex = IndexOfReference(_topLevelNodes, anchor, 0);
            if (anchorIndex >= 0)
                position = anchorIndex;
        }

        _topLevelNodes.Insert(position, node);
        SyncStationsFromTree();
        RebuildDisplayRows();
        OnPropertyChanged(nameof(Groups));
        PersistStationList();
    }

    private static void SetNodeGroupId(object node, string? groupId)
    {
        switch (node)
        {
            case RadioStation station:
                station.GroupId = groupId;
                break;
            case StationDivider divider:
                divider.GroupId = groupId;
                break;
        }
    }

    /// <summary>
    /// Rebuilds the arrangement from the rows after a drag has reordered them.
    /// <para>
    /// The list control performs a reorder by mutating the collection it is bound to, so by
    /// the time this runs <see cref="DisplayRows"/> already holds the new order and is the
    /// input, not the output.
    /// </para>
    /// </summary>
    public void ApplyDisplayReorder()
    {
        ReplaceTopLevelNodes(StationLayoutPolicy.ApplyReorder(_topLevelNodes, [.. DisplayRows]));
    }

    /// <summary>
    /// Replaces the arrangement, then refreshes everything derived from it.
    /// </summary>
    public void ReplaceTopLevelNodes(IEnumerable<object> nodes)
    {
        _topLevelNodes.Clear();
        _topLevelNodes.AddRange(nodes);
        SyncStationsFromTree();
        RebuildDisplayRows();
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(TopLevelNodes));
    }

    /// <summary>
    /// Removes an item from the arrangement, wherever it sits. Folders are removed with their
    /// contents already emptied by the caller.
    /// </summary>
    private void RemoveFromTree(object item)
    {
        if (_topLevelNodes.Remove(item))
            return;

        foreach (object node in _topLevelNodes)
        {
            if (node is StationGroup group && group.Children.Remove(item))
            {
                group.NotifyChildrenChanged();
                return;
            }
        }
    }

    private static int IndexOfReference(IList<object> items, object target, int startIndex)
    {
        for (int i = startIndex; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], target))
                return i;
        }
        return -1;
    }

    private static int IndexOfReference(IList<RadioStation> items, RadioStation target, int startIndex)
    {
        for (int i = startIndex; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], target))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Save the current stations list to settings <em>and</em> re-point the player at the
    /// selected station.
    /// <para>
    /// This is the "a station's definition was edited" path: callers such as the station
    /// editor rely on the re-transition to pick up a changed stream URL, volume or buffer
    /// level. If you only need the list written to disk, call
    /// <see cref="PersistStationList"/> instead - restarting the stream for a reorder is a
    /// defect, not a side benefit.
    /// </para>
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
    /// Persist which station is selected. Safe to call after a reorder: selection is keyed
    /// on the station's id, so it survives the list moving underneath it.
    /// </summary>
    public void UpdateSelectedStationId()
    {
        if (_selectedStation is null)
            return;

        _stationService.SaveSelectedStation(_selectedStation.Id, Stations.IndexOf(_selectedStation));
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

        // Cycle through what the user can see, in the order they see it. Once a folder is
        // collapsed or a view sort is on, "next" means the next row on screen; stepping through
        // the stored order instead would jump to stations that are not even visible.
        IReadOnlyList<RadioStation> order = DisplayOrderedStations;
        if (order.Count < 2)
        {
            // Everything is tucked away inside collapsed folders. Fall back to the full list
            // rather than refusing to change station.
            order = Stations;
        }

        int currentIndex = -1;
        if (_selectedStation is not null)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], _selectedStation))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        int newIndex = currentIndex >= 0
            ? (currentIndex + direction + order.Count) % order.Count
            : direction > 0
                ? 0
                : order.Count - 1;

        Debug.WriteLine($"[PlayerViewModel] Cycling {directionName} from index {currentIndex} to {newIndex}");
        SelectedStation = order[newIndex];
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
