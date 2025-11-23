using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Trdo.Models;
using Trdo.ViewModels;

namespace Trdo.Pages;

/// <summary>
/// Page for searching radio stations from RadioBrowser API.
/// </summary>
public sealed partial class SearchStation : Page
{
    public SearchStationViewModel ViewModel { get; }
    private ShellViewModel? _shellViewModel;

    public SearchStation()
    {
        InitializeComponent();
        ViewModel = new SearchStationViewModel();
        DataContext = ViewModel;

        Loaded += SearchStation_Loaded;
    }

    private void SearchStation_Loaded(object sender, RoutedEventArgs e)
    {
        // Find the ShellViewModel from the parent page
        _shellViewModel = FindShellViewModel();
        SearchTextBox.Focus(FocusState.Programmatic);
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

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RadioBrowserStation station)
        {
            // Navigate to AddStation page with the selected station
            _shellViewModel?.NavigateToAddStationPage(station);
        }
    }

    private void ManualEntryButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to manual entry page
        _shellViewModel?.NavigateToAddStationPage();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate back without adding
        _shellViewModel?.GoBack();
    }
}
