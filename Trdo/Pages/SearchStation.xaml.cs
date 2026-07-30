using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Trdo.Controls;
using Trdo.Models;
using Trdo.Services;
using Trdo.ViewModels;

namespace Trdo.Pages;

/// <summary>
/// Page for searching radio stations from RadioBrowser API.
/// </summary>
public sealed partial class SearchStation : Page
{
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    public SearchStationViewModel ViewModel { get; }
    private ShellViewModel? _shellViewModel;
    private Button? _activePreviewButton;
    private string? _previewingStationUrl;

    public SearchStation()
    {
        InitializeComponent();
        ViewModel = new SearchStationViewModel();
        DataContext = ViewModel;

        Loaded += SearchStation_Loaded;
        Unloaded += SearchStation_Unloaded;
    }

    private void SearchStation_Loaded(object sender, RoutedEventArgs e)
    {
        // Find the ShellViewModel from the parent page
        _shellViewModel = FindShellViewModel();
        SearchTextBox.Focus(FocusState.Programmatic);

        RadioPlayerService.Instance.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    private void SearchStation_Unloaded(object sender, RoutedEventArgs e)
    {
        RadioPlayerService.Instance.PlaybackStateChanged -= OnPlaybackStateChanged;
        StopPreview();
    }

    private ShellViewModel? FindShellViewModel()
    {
        // Walk up the visual tree to find ShellPage
        DependencyObject current = this;
        while (current != null)
        {
            if (current is ShellPage shellPage)
            {
                return shellPage.ViewModel;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RadioBrowserStation station)
            return;

        string stationUrl = station.GetStreamUrl();

        if (_previewingStationUrl == stationUrl &&
            (RadioPlayerService.Instance.IsPlaying || RadioPlayerService.Instance.IsBuffering))
        {
            // Same station is playing, pause it
            StopPreview();
            return;
        }

        // Reset old preview button icon if switching stations
        if (_activePreviewButton != null && _activePreviewButton != button)
        {
            SetButtonGlyph(_activePreviewButton, PlayGlyph);
        }

        // Start previewing the new station
        RadioPlayerService.Instance.SetStreamUrl(stationUrl);
        RadioPlayerService.Instance.SetStationName(station.Name);
        RadioPlayerService.Instance.Play();

        SetButtonGlyph(button, PauseGlyph);
        _activePreviewButton = button;
        _previewingStationUrl = stationUrl;
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        if (_activePreviewButton == null)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_activePreviewButton != null)
            {
                SetButtonGlyph(_activePreviewButton, isPlaying ? PauseGlyph : PlayGlyph);
            }
        });
    }

    private static void SetButtonGlyph(Button button, string glyph)
    {
        if (button.Content is FontIcon icon)
        {
            icon.Glyph = glyph;
        }
    }

    private void StopPreview()
    {
        if (_previewingStationUrl != null)
        {
            RadioPlayerService.Instance.Pause();
            PlayerViewModel.Shared.RestoreSelectedStationPlaybackTarget();

            if (_activePreviewButton != null)
            {
                SetButtonGlyph(_activePreviewButton, PlayGlyph);
            }

            _activePreviewButton = null;
            _previewingStationUrl = null;
        }
    }

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RadioBrowserStation station)
        {
            StopPreview();

            RadioStation newStation = new()
            {
                Name = station.Name,
                StreamUrl = station.GetStreamUrl(),
                Homepage = !string.IsNullOrWhiteSpace(station.Homepage) ? station.Homepage : null,
                FaviconUrl = !string.IsNullOrWhiteSpace(station.Favicon) ? station.Favicon : null
            };
            PlayerViewModel.Shared.AddStation(newStation);

            // Save right away and return to the main page
            _shellViewModel?.NavigateToPlayingPage();
        }
    }

    private void EditStationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RadioBrowserStation station)
        {
            StopPreview();
            // Navigate to AddStation page pre-filled so details can be edited before saving
            _shellViewModel?.NavigateToAddStationPage(station);
        }
    }

    private async void FilterFlyout_Opening(object sender, object e)
    {
        // Populate the country/language/genre dropdowns the first time the panel opens.
        await ViewModel.LoadFilterOptionsAsync();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFilters();
    }

    private void ManualEntryButton_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        // Open a pop-out window for manual station entry so the flyout closing doesn't clear the fields
        ManualStationWindow addWindow = new();
        WindowHelper.Track(addWindow);
        addWindow.Activate();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        // Navigate back without adding
        _shellViewModel?.GoBack();
    }
}
