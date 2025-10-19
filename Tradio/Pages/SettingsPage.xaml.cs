using Microsoft.UI.Xaml.Controls;
using Trdo.ViewModels;

namespace Trdo.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel();
    }
}
