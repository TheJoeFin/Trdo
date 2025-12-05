using System;
using System.ComponentModel;
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
    /// Opens Discogs search with the current track information.
    /// </summary>
    public async Task SearchOnDiscogs()
    {
        if (!HasMetadata)
            return;

        string url = $"https://www.discogs.com/search?q={DiscogsSearchQuery}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
