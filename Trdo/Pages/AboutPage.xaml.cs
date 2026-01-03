using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.Controls;
using Trdo.ViewModels;

namespace Trdo.Pages;

public sealed partial class AboutPage : Page
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        InitializeComponent();
        ViewModel = new AboutViewModel();
        DataContext = ViewModel;
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenGitHub();
    }

    private void DeveloperGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenDeveloperGitHub();
    }

    private void ReviewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenRatingWindow();
    }

        private void TutorialButton_Click(object sender, RoutedEventArgs e)
        {
            TutorialWindow tutorialWindow = new();
            tutorialWindow.Activate();
        }

        private void RadioBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenRadioBrowser();
        }

        private void WinUIExButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenWinUIEx();
        }

        private void CommunityToolkitButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenCommunityToolkit();
        }
    }
