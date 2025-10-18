using Microsoft.UI.Xaml.Controls;
using Tradio.ViewModels;

namespace Tradio.Controls;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel();
    }
}
