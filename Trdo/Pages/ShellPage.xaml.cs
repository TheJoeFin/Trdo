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

    /// <summary>
    /// Puts the shell back at its root page with an empty back stack. Call this
    /// whenever the hosting window is shown: the page instance lives across
    /// hide/show, so without it the window reopens wherever the user left it.
    /// </summary>
    public void ResetNavigation()
    {
        ViewModel.ResetToPlayingPage();
    }

    /// <summary>
    /// Forces the title bar back to its activated (full colour) visuals.
    /// TitleBar dims each of its parts through "…Deactivated" visual states
    /// driven by window activation; a window that is hidden while deactivated
    /// and then shown again can come back without a fresh activation, leaving
    /// the control stuck in the dimmed state it had when focus was lost.
    /// </summary>
    public void RefreshTitleBarActivation()
    {
        VisualStateManager.GoToState(
            SimpleTitleBar,
            ViewModel.CanGoBack ? "BackButtonVisible" : "BackButtonCollapsed",
            false);
        VisualStateManager.GoToState(SimpleTitleBar, "IconVisible", false);
        VisualStateManager.GoToState(SimpleTitleBar, "TitleTextVisible", false);
    }
}
