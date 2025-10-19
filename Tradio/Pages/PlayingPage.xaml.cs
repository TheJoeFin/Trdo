using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using Trdo.Models;
using Trdo.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Trdo.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PlayingPage : Page
{
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

        // Subscribe to property changes to update UI
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Subscribe to playback errors
        ViewModel.PlaybackError += ViewModel_PlaybackError;

        // Wait for loaded to access named elements
        Loaded += PlayingPage_Loaded;
        
        Debug.WriteLine("=== PlayingPage Constructor END ===");
    }

    private void PlayingPage_Loaded(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("=== PlayingPage_Loaded START ===");
        Debug.WriteLine($"[PlayingPage] Current IsPlaying: {ViewModel.IsPlaying}");
        Debug.WriteLine($"[PlayingPage] Current SelectedStation: {ViewModel.SelectedStation?.Name ?? "null"}");
        Debug.WriteLine($"[PlayingPage] Current StreamUrl: {ViewModel.StreamUrl}");
        
        UpdatePlayButtonState();
        UpdateStationSelection();

        // Find the ShellViewModel from the parent page
        _shellViewModel = FindShellViewModel();
        Debug.WriteLine($"[PlayingPage] ShellViewModel found: {_shellViewModel != null}");
        
        Debug.WriteLine("=== PlayingPage_Loaded END ===");
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
        
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            Debug.WriteLine($"[PlayingPage] IsPlaying changed to: {ViewModel.IsPlaying}");
            UpdatePlayButtonState();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.SelectedStation))
        {
            Debug.WriteLine($"[PlayingPage] SelectedStation changed to: {ViewModel.SelectedStation?.Name ?? "null"}");
            UpdateStationSelection();
        }
    }

    private async void ViewModel_PlaybackError(object? sender, string errorMessage)
    {
        Debug.WriteLine($"[PlayingPage] PlaybackError event received: {errorMessage}");
        
        // Display error to user using an InfoBar or ContentDialog
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Playback Error",
                Content = errorMessage,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayingPage] EXCEPTION showing error dialog: {ex.Message}");
            // If dialog fails, silently ignore
        }
    }

    private void UpdatePlayButtonState()
    {
        var playIcon = this.FindName("PlayIcon") as FontIcon;
        var playText = this.FindName("PlayText") as TextBlock;

        if (playIcon != null && playText != null)
        {
            if (ViewModel.IsPlaying)
            {
                playIcon.Glyph = "\uE769"; // Pause icon
                playText.Text = "Pause";
                Debug.WriteLine("[PlayingPage] Play button updated to 'Pause'");
            }
            else
            {
                playIcon.Glyph = "\uE768"; // Play icon
                playText.Text = "Play";
                Debug.WriteLine("[PlayingPage] Play button updated to 'Play'");
            }
        }
        else
        {
            Debug.WriteLine($"[PlayingPage] WARNING: Play button elements not found (PlayIcon={playIcon != null}, PlayText={playText != null})");
        }
    }

    private void UpdateStationSelection()
    {
        Debug.WriteLine("[PlayingPage] UpdateStationSelection called");
        Debug.WriteLine($"[PlayingPage] Selected station: {ViewModel.SelectedStation?.Name ?? "null"}");
        
        // Find all station buttons and update their selection state
        if (StationsItemsControl == null)
        {
            Debug.WriteLine("[PlayingPage] WARNING: StationsItemsControl is null");
            return;
        }

        for (int i = 0; i < ViewModel.Stations.Count; i++)
        {
            FrameworkElement? container = StationsItemsControl.ContainerFromIndex(i) as FrameworkElement;
            if (container == null)
            {
                continue;
            }
            Button? button = FindDescendant<Button>(container);
            if (button != null && button.Tag is RadioStation station)
            {
                Border? indicator = FindDescendant<Border>(button, "SelectionIndicator");
                if (indicator != null)
                {
                    bool isSelected = station == ViewModel.SelectedStation;
                    indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                    Debug.WriteLine($"[PlayingPage] Station '{station.Name}' selection indicator: {(isSelected ? "Visible" : "Collapsed")}");
                }
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

    private void StationButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("=== StationButton_Click START ===");
        
        if (sender is Button button && button.Tag is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Station button clicked: {station.Name}");
            Debug.WriteLine($"[PlayingPage] Station URL: {station.StreamUrl}");
            Debug.WriteLine($"[PlayingPage] Current selected station before change: {ViewModel.SelectedStation?.Name ?? "null"}");
            
            ViewModel.SelectedStation = station;
            
            Debug.WriteLine($"[PlayingPage] Current selected station after change: {ViewModel.SelectedStation?.Name ?? "null"}");
        }
        else
        {
            Debug.WriteLine($"[PlayingPage] WARNING: StationButton_Click - Invalid sender or Tag (sender type: {sender?.GetType().Name}, Tag type: {(sender as Button)?.Tag?.GetType().Name})");
        }
        
        Debug.WriteLine("=== StationButton_Click END ===");
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

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Quit button clicked");
        Application.Current.Exit();
    }

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[PlayingPage] Add station button clicked");
        
        // Use navigation service if available
        if (_shellViewModel != null)
        {
            Debug.WriteLine("[PlayingPage] Navigating to Add Station page via ShellViewModel");
            _shellViewModel.NavigateToAddStationPage();
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

    private void EditStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            Debug.WriteLine($"[PlayingPage] Edit station clicked: {station.Name}");
            // Navigate to AddStation page in edit mode with the station data
            _shellViewModel?.NavigateToAddStationPage(station);
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
}
