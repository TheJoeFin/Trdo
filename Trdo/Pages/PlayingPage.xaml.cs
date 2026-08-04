using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using Trdo.Controls;
using Trdo.Models;
using Trdo.Services;
using Trdo.ViewModels;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Trdo.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PlayingPage : Page
{
    private const int MinIndexForScrolling = 3;
    private static readonly TimeSpan NowPlayingMarqueeDelay = TimeSpan.FromSeconds(2);
    private const string FilledStar = "\uE735";
    private const string OutlineStar = "\uE734";

    private readonly FavoritesService _favoritesService = FavoritesService.Instance;
    private readonly PlaybackErrorService _errorService = PlaybackErrorService.Instance;
    private readonly DispatcherQueueTimer _nowPlayingMarqueeDelayTimer;

    /// <summary>
    /// The playback error dialog currently on screen, kept so it can be taken back
    /// down if the failure it describes stops being true while the user is reading it.
    /// </summary>
    private ContentDialog? _playbackErrorDialog;

    public PlayerViewModel ViewModel { get; }
    private ShellViewModel? _shellViewModel;

    public PlayingPage()
    {
        Debug.WriteLine("=== PlayingPage Constructor START ===");

        InitializeComponent();
        // Use shared instance so all pages reference the same ViewModel
        ViewModel = PlayerViewModel.Shared;
        DataContext = ViewModel;
        Debug.WriteLine("[PlayingPage] ViewModel assigned and DataContext set");
        NowPlayingMarqueeText.MarqueeCompleted += NowPlayingMarqueeText_MarqueeCompleted;

        // Subscribe to property changes to update UI
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Act as the presenter for playback errors. PlaybackErrorService only shows an
        // error while something is subscribed here, so this pairs strictly with the
        // unsubscribe in Unloaded.
        _errorService.ErrorPresented += ErrorService_ErrorPresented;
        _errorService.ErrorWithdrawn += ErrorService_ErrorWithdrawn;

        // Subscribe to favorites changes
        _favoritesService.FavoritesChanged += FavoritesService_FavoritesChanged;

        // Wait for loaded to access named elements
        Loaded += PlayingPage_Loaded;
        Unloaded += PlayingPage_Unloaded;

        _nowPlayingMarqueeDelayTimer = DispatcherQueue.CreateTimer();
        _nowPlayingMarqueeDelayTimer.Interval = NowPlayingMarqueeDelay;
        _nowPlayingMarqueeDelayTimer.IsRepeating = false;
        _nowPlayingMarqueeDelayTimer.Tick += NowPlayingMarqueeDelayTimer_Tick;

        Debug.WriteLine("=== PlayingPage Constructor END ===");
    }

    private void PlayingPage_Loaded(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("=== PlayingPage_Loaded START ===");
        Debug.WriteLine($"[PlayingPage] Current IsPlaying: {ViewModel.IsPlaying}");
        Debug.WriteLine($"[PlayingPage] Current SelectedStation: {ViewModel.SelectedStation?.Name ?? "null"}");
        Debug.WriteLine($"[PlayingPage] Current StreamUrl: {ViewModel.StreamUrl}");

        UpdateStationSelection();
        UpdateFavoriteButtonState();

        // Restore volume slider visibility from persisted setting
        VolumeControlGrid.Visibility = SettingsService.IsVolumeSliderVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Find the ShellViewModel from the parent page
        _shellViewModel = FindShellViewModel();
        Debug.WriteLine($"[PlayingPage] ShellViewModel found: {_shellViewModel != null}");

        Debug.WriteLine("=== PlayingPage_Loaded END ===");

        UpdateNowPlayingMarqueeState();

        // scroll to selected station
        if (ViewModel.SelectedStation is not null)
        {
            int index = ViewModel.Stations.IndexOf(ViewModel.SelectedStation);
            if (index is >= 0 and > MinIndexForScrolling)
            {
                StationsListView.ScrollIntoView(ViewModel.SelectedStation);
                Debug.WriteLine($"[PlayingPage] Scrolled to selected station at index {index}");
            }
            else if (index < 0)
            {
                Debug.WriteLine("[PlayingPage] WARNING: SelectedStation not found in Stations list");
            }
        }
        else
        {
            Debug.WriteLine("[PlayingPage] No SelectedStation to scroll to");
        }
    }

    private void PlayingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _nowPlayingMarqueeDelayTimer.Stop();
        _nowPlayingMarqueeDelayTimer.Tick -= NowPlayingMarqueeDelayTimer_Tick;
        NowPlayingMarqueeText.MarqueeCompleted -= NowPlayingMarqueeText_MarqueeCompleted;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _errorService.ErrorPresented -= ErrorService_ErrorPresented;
        _errorService.ErrorWithdrawn -= ErrorService_ErrorWithdrawn;
        _favoritesService.FavoritesChanged -= FavoritesService_FavoritesChanged;
        SetNowPlayingScrolling(false);
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

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Debug.WriteLine($"[PlayingPage] ViewModel PropertyChanged: {e.PropertyName}");
        UpdateStationSelection();

        if (e.PropertyName is (nameof(PlayerViewModel.CurrentMetadata)) or
                 (nameof(PlayerViewModel.HasNowPlaying)))
        {
            UpdateFavoriteButtonState();
        }

        if (e.PropertyName is (nameof(PlayerViewModel.NowPlaying)) or
            (nameof(PlayerViewModel.HasNowPlaying)))
        {
            UpdateNowPlayingMarqueeState();
        }
    }

    private void UpdateFavoriteButtonState()
    {
        if (FavoriteIcon == null)
            return;

        bool isFavorited = _favoritesService.IsFavorited(ViewModel.CurrentMetadata);
        FavoriteIcon.Glyph = isFavorited ? FilledStar : OutlineStar;
        Debug.WriteLine($"[PlayingPage] Favorite button updated. IsFavorited: {isFavorited}");
    }

    /// <summary>
    /// Puts a playback error on screen. Only ever reached once
    /// <see cref="PlaybackErrorService"/> has established that the failure still
    /// describes reality and that this window is visible, so no further checking
    /// belongs here.
    /// </summary>
    private async void ErrorService_ErrorPresented(object? sender, string errorMessage)
    {
        Debug.WriteLine($"[PlayingPage] Presenting playback error: {errorMessage}");

        ContentDialog dialog = new()
        {
            Title = "Playback Error",
            Content = errorMessage,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };

        try
        {
            _playbackErrorDialog = dialog;
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayingPage] EXCEPTION showing error dialog: {ex.Message}");
            // If dialog fails, silently ignore
        }
        finally
        {
            // ShowAsync also returns when the service withdraws the dialog, which clears
            // the field first. Only a genuine dismissal by the user leaves our dialog in
            // place — reporting the other case would clear an error we never showed.
            if (ReferenceEquals(_playbackErrorDialog, dialog))
            {
                _playbackErrorDialog = null;
                _errorService.NotifyErrorDismissed();
            }
        }
    }

    /// <summary>
    /// Takes the error dialog down when the service decides it no longer makes sense —
    /// the station recovered, or the user moved to another one.
    /// </summary>
    private void ErrorService_ErrorWithdrawn(object? sender, EventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Withdrawing playback error dialog");
        _playbackErrorDialog?.Hide();
        _playbackErrorDialog = null;
    }

    private void UpdateStationSelection()
    {
        Debug.WriteLine("[PlayingPage] UpdateStationSelection called");
        Debug.WriteLine($"[PlayingPage] Selected station: {ViewModel.SelectedStation?.Name ?? "null"}");

        // Find all station items and update their selection state
        if (StationsListView == null)
        {
            Debug.WriteLine("[PlayingPage] WARNING: StationsListView is null");
            return;
        }

        // Ensure ListView SelectedItem is synchronized with ViewModel
        if (StationsListView.SelectedItem != ViewModel.SelectedStation)
        {
            StationsListView.SelectedItem = ViewModel.SelectedStation;
            Debug.WriteLine($"[PlayingPage] Synchronized ListView.SelectedItem to {ViewModel.SelectedStation?.Name ?? "null"}");
        }

        for (int i = 0; i < ViewModel.Stations.Count; i++)
        {
            if (StationsListView.ContainerFromIndex(i) is not ListViewItem container)
                continue;

            RadioStation station = ViewModel.Stations[i];
            Border? indicator = FindDescendant<Border>(container, "SelectionIndicator");
            if (indicator != null)
            {
                bool isSelected = station == ViewModel.SelectedStation;
                indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                Debug.WriteLine($"[PlayingPage] Station '{station.Name}' selection indicator: {(isSelected ? "Visible" : "Collapsed")}");
            }
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

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Volume is already bound two-way, but we can handle additional logic here if needed
        Debug.WriteLine($"[PlayingPage] Volume slider changed: {e.NewValue}");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("=== PlayButton_Click START ===");
        Debug.WriteLine($"[PlayingPage] Current IsPlaying: {ViewModel.IsPlaying}");
        Debug.WriteLine($"[PlayingPage] Current selected station: {ViewModel.SelectedStation?.Name ?? "null"}");
        Debug.WriteLine($"[PlayingPage] Current stream URL: {ViewModel.StreamUrl}");

        ViewModel.Toggle();

        Debug.WriteLine($"[PlayingPage] After Toggle - IsPlaying: {ViewModel.IsPlaying}");
        Debug.WriteLine("=== PlayButton_Click END ===");
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Pop-out button clicked");

        if (Application.Current is App app)
        {
            app.ShowMiniPlayerWindow();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Close button clicked");
        // Persist any pending per-station volume change before quitting.
        ViewModel.FlushStationsSave();
        Application.Current.Exit();
    }

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Add station button clicked");

        // Use navigation service if available
        if (_shellViewModel != null)
        {
            Debug.WriteLine("[PlayingPage] Navigating to Search Station page via ShellViewModel");
            _shellViewModel.NavigateToSearchStationPage();
        }
        else
        {
            Debug.WriteLine("[PlayingPage] ShellViewModel not available, showing fallback dialog");
            // Fallback to dialog
            ShowAddStationDialog();
        }
    }

    private async void ShowAddStationDialog()
    {
        ContentDialog dialog = new()
        {
            Title = "Add Station",
            Content = "Add station functionality coming soon!",
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Settings button clicked");
        // Use navigation service if available
        _shellViewModel?.NavigateToSettingsPage();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Info button clicked");
        // Navigate to About page
        _shellViewModel?.NavigateToAboutPage();
    }

    private void NowPlayingInfo_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Now Playing info clicked");
        // Navigate to Now Playing details page
        _shellViewModel?.NavigateToNowPlayingPage();
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Favorite button clicked");

        if (ViewModel.CurrentMetadata?.HasMetadata != true)
        {
            Debug.WriteLine("[PlayingPage] No metadata to favorite");
            return;
        }

        string stationName = ViewModel.SelectedStation?.Name ?? "Unknown Station";
        bool isFavorited = _favoritesService.ToggleFavorite(ViewModel.CurrentMetadata, stationName);
        Debug.WriteLine($"[PlayingPage] Track favorite toggled. IsFavorited: {isFavorited}");

        // UpdateFavoriteButtonState will be called via the FavoritesChanged event
    }

    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Favorites button clicked");
        _shellViewModel?.NavigateToFavoritesPage();
    }

    private void VisitSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Visit Station Site clicked: {station.Name}");
            // Navigate to AddStation page in edit mode with the station data
            ViewModel.VisitWebsite(station);
        }
    }

    private void EditStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Edit station clicked: {station.Name}");
            // Open a pop-out window for editing so the flyout closing doesn't clear the fields
            ManualStationWindow editWindow = new();
            WindowHelper.Track(editWindow);
            editWindow.LoadStationForEdit(station);
            editWindow.Activate();
        }
    }

    private void RemoveStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Remove station clicked: {station.Name}");
            // Remove the station immediately - no dialog since it's in a flyout
            ViewModel.RemoveStation(station);
        }
    }

    private void StationsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        Debug.WriteLine("[PlayingPage] DragItemsCompleted - Station order changed");
        // Save the new order to persistent storage
        ViewModel.SaveStations();

        // Update the selected station index since the order might have changed
        ViewModel.UpdateSelectedStationIndex();
    }

    private void StationsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Debug.WriteLine("=== StationsListView_SelectionChanged START ===");

        if (sender is ListView stations && stations.SelectedItem is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Station clicked: {station.Name}");
            Debug.WriteLine($"[PlayingPage] Station URL: {station.StreamUrl}");
            Debug.WriteLine($"[PlayingPage] Current selected station before change: {ViewModel.SelectedStation?.Name ?? "null"}");

            ViewModel.SelectedStation = station;

            Debug.WriteLine($"[PlayingPage] Current selected station after change: {ViewModel.SelectedStation?.Name ?? "null"}");
        }
        else
        {
            Debug.WriteLine($"[PlayingPage] WARNING: StationsListView_SelectionChanged - Invalid item type: {sender?.GetType().Name}");
        }

        Debug.WriteLine("=== StationsListView_SelectionChanged END ===");
    }

    private void VolumeControl_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        double change = (delta / 120.0) * 0.02;
        ViewModel.Volume = Math.Clamp(ViewModel.Volume + change, 0, 2);
        e.Handled = true;
    }

    private void HideVolumeSlider_Click(object sender, RoutedEventArgs e)
    {
        VolumeControlGrid.Visibility = Visibility.Collapsed;
        SettingsService.IsVolumeSliderVisible = false;
    }

    private void ShowVolumeSlider_Click(object sender, RoutedEventArgs e)
    {
        VolumeControlGrid.Visibility = Visibility.Visible;
        SettingsService.IsVolumeSliderVisible = true;
    }

    private void PageContextMenu_Opening(object sender, object e)
    {
        if (VolumeControlGrid.Visibility == Visibility.Visible)
        {
            ((MenuFlyout)sender).Hide();
        }
    }

    private void NowPlayingTextHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNowPlayingMarqueeState();
    }

    private void NowPlayingMarqueeText_MarqueeCompleted(object? sender, object args) =>
        RestartNowPlayingMarqueeAfterDelay();

    private void FavoritesService_FavoritesChanged(object? sender, EventArgs args) =>
        UpdateFavoriteButtonState();

    private void NowPlayingMarqueeDelayTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        if (!ShouldUseNowPlayingMarquee())
        {
            SetNowPlayingScrolling(false);
            return;
        }

        SetNowPlayingScrolling(true);
    }

    private void UpdateNowPlayingMarqueeState()
    {
        _nowPlayingMarqueeDelayTimer.Stop();
        SetNowPlayingScrolling(false);

        if (!ShouldUseNowPlayingMarquee())
        {
            return;
        }

        _nowPlayingMarqueeDelayTimer.Start();
    }

    private void RestartNowPlayingMarqueeAfterDelay()
    {
        if (!ShouldUseNowPlayingMarquee())
        {
            return;
        }

        _nowPlayingMarqueeDelayTimer.Stop();
        _nowPlayingMarqueeDelayTimer.Start();
    }

    private bool ShouldUseNowPlayingMarquee()
    {
        return IsLoaded &&
               ViewModel.HasNowPlaying &&
               !string.IsNullOrWhiteSpace(ViewModel.NowPlaying) &&
               DoesNowPlayingOverflow();
    }

    private bool DoesNowPlayingOverflow()
    {
        if (NowPlayingTextHost.ActualWidth <= 0)
        {
            return false;
        }

        TextBlock measurementTextBlock = new()
        {
            CharacterSpacing = NowPlayingText.CharacterSpacing,
            FontFamily = NowPlayingText.FontFamily,
            FontSize = NowPlayingText.FontSize,
            FontStretch = NowPlayingText.FontStretch,
            FontStyle = NowPlayingText.FontStyle,
            FontWeight = NowPlayingText.FontWeight,
            Text = ViewModel.NowPlaying,
            TextWrapping = TextWrapping.NoWrap
        };

        measurementTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return measurementTextBlock.DesiredSize.Width > NowPlayingTextHost.ActualWidth;
    }

    private void SetNowPlayingScrolling(bool isScrolling)
    {
        if (isScrolling)
        {
            NowPlayingText.Visibility = Visibility.Collapsed;
            NowPlayingMarqueeText.Visibility = Visibility.Visible;
            if (IsLoaded)
            {
                NowPlayingMarqueeText.StartMarquee();
            }
            return;
        }

        NowPlayingMarqueeText.Visibility = Visibility.Collapsed;
        NowPlayingText.Visibility = Visibility.Visible;
        if (IsLoaded)
        {
            NowPlayingMarqueeText.StopMarquee();
        }
    }
}
