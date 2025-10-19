using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Trdo.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; }

    public ShellPage()
    {
        InitializeComponent();
        ViewModel = new ShellViewModel();
        DataContext = ViewModel;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Set the ContentFrame reference in the ViewModel
        ViewModel.ContentFrame = ContentFrame;

        // Navigate to PlayingPage on load
        ViewModel.NavigateToPlayingPage();
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        ViewModel.GoBack();
    }
}
