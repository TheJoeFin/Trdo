using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;
using Windows.System;

namespace Trdo.ViewModels;

public class NowPlayingViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private readonly FavoritesService _favoritesService = FavoritesService.Instance;
    private readonly PlaylistHistoryService _historyService = PlaylistHistoryService.Instance;

    private bool _isCurrentTrackFavorited;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsCurrentTrackFavorited
    {
        get => _isCurrentTrackFavorited;
        set
        {
            if (_isCurrentTrackFavorited == value) return;
            _isCurrentTrackFavorited = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The playlist history showing recent tracks (from singleton service).
    /// </summary>
    public ObservableCollection<PlaylistHistoryItem> PlaylistHistory => _historyService.History;

    public NowPlayingViewModel()
    {
        // Subscribe to metadata changes for UI updates
        _player.StreamMetadataChanged += OnStreamMetadataChanged;

        // Subscribe to favorites changes to update UI
        _favoritesService.FavoritesChanged += (_, _) =>
        {
            UpdateCurrentTrackFavoriteStatus();
        };

        // Initialize current track favorite status
        UpdateCurrentTrackFavoriteStatus();

        Debug.WriteLine($"[NowPlayingViewModel] Initialized with {PlaylistHistory.Count} history items from service");
    }

    private void OnStreamMetadataChanged(object? sender, StreamMetadata metadata)
    {
        OnPropertyChanged(nameof(CurrentMetadata));
        OnPropertyChanged(nameof(StreamTitle));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(HasMetadata));
        OnPropertyChanged(nameof(HasArtist));
        OnPropertyChanged(nameof(HasTitle));
        OnPropertyChanged(nameof(ShowStreamTitleOnly));
        OnPropertyChanged(nameof(ShowRawStreamTitle));
        OnPropertyChanged(nameof(DiscogsSearchQuery));
        OnPropertyChanged(nameof(SpotifySearchQuery));
        OnPropertyChanged(nameof(IsSpotifyEnabled));
        OnPropertyChanged(nameof(IsDiscogsEnabled));
        OnPropertyChanged(nameof(IsAppleMusicEnabled));
        OnPropertyChanged(nameof(IsYouTubeMusicEnabled));

        // History is now managed by PlaylistHistoryService singleton
        UpdateCurrentTrackFavoriteStatus();
    }

    private void UpdateCurrentTrackFavoriteStatus()
    {
        IsCurrentTrackFavorited = _favoritesService.IsFavorited(CurrentMetadata);
    }

    public void ToggleCurrentTrackFavorite()
    {
        if (CurrentMetadata?.HasMetadata != true)
            return;

        string stationName = PlayerViewModel.Shared.SelectedStation?.Name ?? "Unknown Station";
        IsCurrentTrackFavorited = _favoritesService.ToggleFavorite(CurrentMetadata, stationName);
        Debug.WriteLine($"[NowPlayingViewModel] Toggled favorite for current track. IsFavorited: {IsCurrentTrackFavorited}");
    }

    public void ToggleHistoryItemFavorite(PlaylistHistoryItem? item)
    {
        if (item == null)
            return;

        item.ToggleFavorite();
        Debug.WriteLine($"[NowPlayingViewModel] Toggled favorite for history item: {item.DisplayText}. IsFavorited: {item.IsFavorited}");

        // If this is the current track, update that status too
        if (CurrentMetadata != null && item.UniqueKey == $"{CurrentMetadata.Artist?.ToLowerInvariant()}|{CurrentMetadata.Title?.ToLowerInvariant()}|{CurrentMetadata.StreamTitle?.ToLowerInvariant()}".Trim())
        {
            UpdateCurrentTrackFavoriteStatus();
        }
    }

    /// <summary>
    /// Gets the current stream metadata.
    /// </summary>
    public StreamMetadata CurrentMetadata => _player.CurrentMetadata;

    /// <summary>
    /// Gets the full stream title string.
    /// </summary>
    public string StreamTitle => CurrentMetadata?.StreamTitle ?? string.Empty;

    /// <summary>
    /// Gets the artist name if available.
    /// </summary>
    public string Artist => CurrentMetadata?.Artist ?? string.Empty;

    /// <summary>
    /// Gets the song/track title if available.
    /// </summary>
    public string Title => CurrentMetadata?.Title ?? string.Empty;

    /// <summary>
    /// Gets the display-friendly now playing text.
    /// </summary>
    public string DisplayText => CurrentMetadata?.DisplayText ?? string.Empty;

    /// <summary>
    /// Indicates whether any meaningful metadata is available.
    /// </summary>
    public bool HasMetadata => CurrentMetadata?.HasMetadata ?? false;

    /// <summary>
    /// Indicates whether artist information is available.
    /// </summary>
    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);

    /// <summary>
    /// Indicates whether title information is available.
    /// </summary>
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Indicates whether to show only the raw stream title (when we couldn't parse artist/title).
    /// </summary>
    public bool ShowStreamTitleOnly => HasMetadata && !HasArtist && !HasTitle && !string.IsNullOrWhiteSpace(StreamTitle);

    /// <summary>
    /// Indicates whether to show the raw stream title section (only when we have parsed data to compare).
    /// </summary>
    public bool ShowRawStreamTitle => HasMetadata && (HasArtist || HasTitle) && !string.IsNullOrWhiteSpace(StreamTitle);

    /// <summary>
    /// Gets the search query for Discogs, URL-encoded.
    /// </summary>
    public string DiscogsSearchQuery
    {
        get
        {
            string searchText = DisplayText;
            if (string.IsNullOrWhiteSpace(searchText))
                searchText = StreamTitle;

            return Uri.EscapeDataString(searchText);
        }
    }

    /// <summary>
    /// Gets the search query for Spotify, URL-encoded.
    /// </summary>
    public string SpotifySearchQuery
    {
        get
        {
            string searchText = DisplayText;
            if (string.IsNullOrWhiteSpace(searchText))
                searchText = StreamTitle;

            return Uri.EscapeDataString(searchText);
        }
    }

    /// <summary>
    /// Opens Discogs search with the current track information.
    /// </summary>
    public async Task SearchOnDiscogs()
    {
        if (!HasMetadata)
            return;

        string url = $"https://www.discogs.com/search?q={DiscogsSearchQuery}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    /// <summary>
    /// Opens Spotify search with the current track information.
    /// Tries to open the local Spotify app first, falls back to web.
    /// </summary>
    public async Task SearchOnSpotify()
    {
        if (!HasMetadata)
            return;

        // Try to open the Spotify app first using the spotify: URI scheme
        string spotifyAppUri = $"spotify:search:{SpotifySearchQuery}";

        try
        {
            bool success = await Launcher.LaunchUriAsync(new Uri(spotifyAppUri));

            if (!success)
            {
                // Spotify app not installed or couldn't launch, fall back to web
                Debug.WriteLine("[NowPlayingViewModel] Spotify app not available, falling back to web");
                await OpenSpotifyWeb();
            }
        }
        catch (Exception ex)
        {
            // URI scheme not recognized or other error, fall back to web
            Debug.WriteLine($"[NowPlayingViewModel] Error launching Spotify app: {ex.Message}");
            await OpenSpotifyWeb();
        }
    }

    /// <summary>
    /// Opens Spotify web search as a fallback.
    /// </summary>
    private async Task OpenSpotifyWeb()
    {
        string webUrl = $"https://open.spotify.com/search/{SpotifySearchQuery}";
        await Launcher.LaunchUriAsync(new Uri(webUrl));
    }

    /// <summary>
    /// Opens Apple Music search with the current track information.
    /// Tries to open the local Apple Music app first, falls back to web.
    /// </summary>
    public async Task SearchOnAppleMusic()
    {
        if (!HasMetadata)
            return;

        string query = Uri.EscapeDataString(DisplayText.Length > 0 ? DisplayText : StreamTitle);

        // Try to open the Apple Music app using the itmss: URI scheme
        string appleMusicAppUri = $"itmss://music.apple.com/search?term={query}";
        try
        {
            bool success = await Launcher.LaunchUriAsync(new Uri(appleMusicAppUri));
            if (!success)
            {
                Debug.WriteLine("[NowPlayingViewModel] Apple Music app not available, falling back to web");
                await OpenAppleMusicWeb(query);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NowPlayingViewModel] Error launching Apple Music app: {ex.Message}");
            await OpenAppleMusicWeb(query);
        }
    }

    private async Task OpenAppleMusicWeb(string query)
    {
        string webUrl = $"https://music.apple.com/search?term={query}";
        await Launcher.LaunchUriAsync(new Uri(webUrl));
    }

    /// <summary>
    /// Opens YouTube Music search with the current track information.
    /// </summary>
    public async Task SearchOnYouTubeMusic()
    {
        if (!HasMetadata)
            return;

        string query = Uri.EscapeDataString(DisplayText.Length > 0 ? DisplayText : StreamTitle);
        string url = $"https://music.youtube.com/search?q={query}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    /// <summary>
    /// Gets whether Spotify search links should be shown.
    /// </summary>
    public bool IsSpotifyEnabled => SettingsService.IsSpotifyEnabled;

    /// <summary>
    /// Gets whether Discogs search links should be shown.
    /// </summary>
    public bool IsDiscogsEnabled => SettingsService.IsDiscogsEnabled;

    /// <summary>
    /// Gets whether Apple Music search links should be shown.
    /// </summary>
    public bool IsAppleMusicEnabled => SettingsService.IsAppleMusicEnabled;

    /// <summary>
    /// Gets whether YouTube Music search links should be shown.
    /// </summary>
    public bool IsYouTubeMusicEnabled => SettingsService.IsYouTubeMusicEnabled;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
