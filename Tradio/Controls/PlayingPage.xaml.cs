using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Tradio.Models;
using Tradio.ViewModels;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tradio.Controls;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PlayingPage : Page
{
    public PlayerViewModel ViewModel { get; }
    private ShellViewModel? _shellViewModel;

    public PlayingPage()
    {
        InitializeComponent();
        // Use shared instance so all pages reference the same ViewModel
        ViewModel = PlayerViewModel.Shared;
        DataContext = ViewModel;

        // Subscribe to property changes to update UI
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Wait for loaded to access named elements
        Loaded += PlayingPage_Loaded;
    }

    private void PlayingPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePlayButtonState();
        UpdateStationSelection();
        
        // Find the ShellViewModel from the parent page
        _shellViewModel = FindShellViewModel();
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
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            UpdatePlayButtonState();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.SelectedStation))
        {
            UpdateStationSelection();
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
            }
            else
            {
                playIcon.Glyph = "\uE768"; // Play icon
                playText.Text = "Play";
            }
        }
    }

    private void UpdateStationSelection()
    {
        // Find all station buttons and update their selection state
        if (StationsItemsControl == null) return;

        for (int i = 0; i < ViewModel.Stations.Count; i++)
        {
            var container = StationsItemsControl.ContainerFromIndex(i) as FrameworkElement;
            if (container != null)
            {
                var button = FindDescendant<Button>(container);
                if (button != null && button.Tag is RadioStation station)
                {
                    var indicator = FindDescendant<Border>(button, "SelectionIndicator");
                    if (indicator != null)
                    {
                        indicator.Visibility = station == ViewModel.SelectedStation
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }
                }
            }
        }
    }

    private T? FindDescendant<T>(DependencyObject parent, string name = "") where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is T typedChild)
            {
                if (string.IsNullOrEmpty(name) || (child is FrameworkElement fe && fe.Name == name))
                {
                    return typedChild;
                }
            }

            var result = FindDescendant<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void StationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RadioStation station)
        {
            ViewModel.SelectedStation = station;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Volume is already bound two-way, but we can handle additional logic here if needed
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Toggle();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        // Use navigation service if available
        if (_shellViewModel != null)
        {
            _shellViewModel.NavigateToAddStationPage();
        }
        else
        {
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
        // Use navigation service if available
        if (_shellViewModel != null)
        {
            _shellViewModel.NavigateToSettingsPage();
        }
    }

    private void EditStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            // Navigate to AddStation page in edit mode with the station data
            if (_shellViewModel != null)
            {
                _shellViewModel.NavigateToAddStationPage(station);
            }
        }
    }

    private void RemoveStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is RadioStation station)
        {
            // Remove the station immediately - no dialog since it's in a flyout
            ViewModel.RemoveStation(station);
        }
    }
}
