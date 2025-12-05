using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;
using Windows.System;

namespace Trdo.ViewModels;

public partial class NowPlayingViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public NowPlayingViewModel()
    {
        // Subscribe to metadata changes
        _player.StreamMetadataChanged += (_, _) =>
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
        };
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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
