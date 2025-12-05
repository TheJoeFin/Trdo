using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Trdo.ViewModels;

namespace Trdo.Pages;

/// <summary>
/// A page that displays detailed stream metadata (now playing) information.
/// </summary>
public sealed partial class NowPlayingPage : Page
{
    public NowPlayingViewModel ViewModel { get; }

    public NowPlayingPage()
    {
        Debug.WriteLine("=== NowPlayingPage Constructor START ===");

        InitializeComponent();
        ViewModel = new NowPlayingViewModel();
        DataContext = ViewModel;

        Debug.WriteLine("[NowPlayingPage] ViewModel created and DataContext set");
        Debug.WriteLine($"[NowPlayingPage] Current metadata: {ViewModel.DisplayText}");
        Debug.WriteLine("=== NowPlayingPage Constructor END ===");
    }

    private async void DiscogsLink_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[NowPlayingPage] Discogs link clicked");
        await ViewModel.SearchOnDiscogs();
    }

    private async void SpotifyLink_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[NowPlayingPage] Spotify link clicked");
        await ViewModel.SearchOnSpotify();
    }
}
