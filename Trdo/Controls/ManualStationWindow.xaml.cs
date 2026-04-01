using Trdo.Models;
using Trdo.Services;
using Trdo.ViewModels;
using WinUIEx;

namespace Trdo.Controls;

/// <summary>
/// A small standalone window for manually adding or editing a radio station.
/// Opens as a pop-out window so that closing the tray flyout does not clear the form fields.
/// </summary>
public sealed partial class ManualStationWindow : WindowEx
{
    public AddStationViewModel ViewModel { get; }

    public ManualStationWindow()
    {
        InitializeComponent();

        ViewModel = new AddStationViewModel();
        ViewModel.SetPlayerViewModel(PlayerViewModel.Shared);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ModernTitlebar);

        Activated += ManualStationWindow_Activated;
    }

    /// <summary>
    /// Opens the window pre-filled with the given station's data for editing.
    /// </summary>
    public void LoadStationForEdit(RadioStation station)
    {
        ViewModel.LoadStationForEdit(station);
    }

    private void ManualStationWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        // Focus the station name field once the window is ready
        if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            WindowPlacementService.PositionWindowNearAnchor(this, 400, 500);
            StationNameTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            Activated -= ManualStationWindow_Activated;
        }
    }

    private void SaveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.Save())
        {
            Close();
        }
    }

    private void CancelButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Close();
    }
}
