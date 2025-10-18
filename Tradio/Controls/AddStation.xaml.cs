using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tradio.Models;
using Tradio.ViewModels;

namespace Tradio.Controls;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AddStation : Page
{
    public AddStationViewModel ViewModel { get; }
    private ShellViewModel? _shellViewModel;

    public AddStation()
    {
        InitializeComponent();
        ViewModel = new AddStationViewModel();
        DataContext = ViewModel;
        
        // Use the shared PlayerViewModel instance
        ViewModel.SetPlayerViewModel(PlayerViewModel.Shared);

        Loaded += AddStation_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        // Check if we're editing an existing station
        if (e.Parameter is RadioStation station)
        {
            ViewModel.LoadStationForEdit(station);
        }
    }

    private void AddStation_Loaded(object sender, RoutedEventArgs e)
    {
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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Save())
        {
            // Navigate back after successful save
            _shellViewModel?.GoBack();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate back without saving
        _shellViewModel?.GoBack();
    }
}
