using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using Trdo.Models;
using Trdo.ViewModels;
using Windows.System;

namespace Trdo.Pages;

/// <summary>
/// A page that displays the user's favorited tracks.
/// </summary>
public sealed partial class FavoritesPage : Page
{
    private ListViewItem? _previouslySelectedContainer;

    public FavoritesViewModel ViewModel { get; }

    public FavoritesPage()
    {
        Debug.WriteLine("=== FavoritesPage Constructor START ===");

        InitializeComponent();
        ViewModel = new FavoritesViewModel();
        DataContext = ViewModel;

        Debug.WriteLine($"[FavoritesPage] ViewModel created with {ViewModel.Favorites.Count} favorites");
        Debug.WriteLine("=== FavoritesPage Constructor END ===");
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is FavoriteTrack track)
        {
            Debug.WriteLine($"[FavoritesPage] Remove clicked for: {track.DisplayText}");
            ViewModel.RemoveFavorite(track);
        }
    }

    private void FavoritesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Collapse the previously selected item
        if (_previouslySelectedContainer != null)
        {
            StackPanel? previousExpandedContent = FindDescendant<StackPanel>(_previouslySelectedContainer, "ExpandedContent");
            if (previousExpandedContent != null)
            {
                previousExpandedContent.Visibility = Visibility.Collapsed;
            }
        }

        // Expand the newly selected item
        if (sender is ListView listView && listView.SelectedItem is FavoriteTrack)
        {
            ListViewItem? container = listView.ContainerFromItem(listView.SelectedItem) as ListViewItem;
            if (container != null)
            {
                StackPanel? expandedContent = FindDescendant<StackPanel>(container, "ExpandedContent");
                if (expandedContent != null)
                {
                    expandedContent.Visibility = Visibility.Visible;

                    // Apply per-service visibility based on settings
                    SetButtonVisibility(container, "SpotifyButton", Trdo.Services.SettingsService.IsSpotifyEnabled);
                    SetButtonVisibility(container, "DiscogsButton", Trdo.Services.SettingsService.IsDiscogsEnabled);
                    SetButtonVisibility(container, "AppleMusicButton", Trdo.Services.SettingsService.IsAppleMusicEnabled);
                    SetButtonVisibility(container, "YouTubeMusicButton", Trdo.Services.SettingsService.IsYouTubeMusicEnabled);
                }
                _previouslySelectedContainer = container;
            }
        }
        else
        {
            _previouslySelectedContainer = null;
        }
    }

    private async void SpotifyLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is FavoriteTrack track)
        {
            Debug.WriteLine($"[FavoritesPage] Spotify search for: {track.DisplayText}");
            string searchQuery = Uri.EscapeDataString(track.DisplayText);

            // Try Spotify app first
            string spotifyAppUri = $"spotify:search:{searchQuery}";
            try
            {
                bool success = await Launcher.LaunchUriAsync(new Uri(spotifyAppUri));
                if (!success)
                {
                    // Fall back to web
                    string webUrl = $"https://open.spotify.com/search/{searchQuery}";
                    await Launcher.LaunchUriAsync(new Uri(webUrl));
                }
            }
            catch
            {
                // Fall back to web
                string webUrl = $"https://open.spotify.com/search/{searchQuery}";
                await Launcher.LaunchUriAsync(new Uri(webUrl));
            }
        }
    }

    private async void DiscogsLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is FavoriteTrack track)
        {
            Debug.WriteLine($"[FavoritesPage] Discogs search for: {track.DisplayText}");
            string searchQuery = Uri.EscapeDataString(track.DisplayText);
            string url = $"https://www.discogs.com/search?q={searchQuery}";
            await Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void AppleMusicLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is FavoriteTrack track)
        {
            Debug.WriteLine($"[FavoritesPage] Apple Music search for: {track.DisplayText}");
            string searchQuery = Uri.EscapeDataString(track.DisplayText);

            // Try Apple Music app first
            string appleMusicAppUri = $"itmss://music.apple.com/search?term={searchQuery}";
            try
            {
                bool success = await Launcher.LaunchUriAsync(new Uri(appleMusicAppUri));
                if (!success)
                {
                    string webUrl = $"https://music.apple.com/search?term={searchQuery}";
                    await Launcher.LaunchUriAsync(new Uri(webUrl));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavoritesPage] Error launching Apple Music app: {ex.Message}");
                string webUrl = $"https://music.apple.com/search?term={searchQuery}";
                await Launcher.LaunchUriAsync(new Uri(webUrl));
            }
        }
    }

    private async void YouTubeMusicLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton button && button.Tag is FavoriteTrack track)
        {
            Debug.WriteLine($"[FavoritesPage] YouTube Music search for: {track.DisplayText}");
            string searchQuery = Uri.EscapeDataString(track.DisplayText);
            string url = $"https://music.youtube.com/search?q={searchQuery}";
            await Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private void SetButtonVisibility(DependencyObject container, string buttonName, bool isVisible)
    {
        HyperlinkButton? button = FindDescendant<HyperlinkButton>(container, buttonName);
        if (button != null)
        {
            button.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private T? FindDescendant<T>(DependencyObject parent, string name = "") where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
            {
                if (string.IsNullOrEmpty(name) || (child is FrameworkElement fe && fe.Name == name))
                {
                    return typedChild;
                }
            }

            T? result = FindDescendant<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
}
