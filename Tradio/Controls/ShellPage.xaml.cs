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
using Tradio.ViewModels;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tradio.Controls;
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
