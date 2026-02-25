using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.ViewModels;

namespace Trdo.Pages;

public sealed partial class SettingsPage : Page
{
    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = GetActiveWindow();
            int count = await ViewModel.ImportStationsAsync(hwnd);

            if (count > 0)
            {
                ImportExportInfoBar.Severity = InfoBarSeverity.Success;
                ImportExportInfoBar.Message = $"Imported {count} station{(count == 1 ? "" : "s")}.";
            }
            else
            {
                ImportExportInfoBar.Severity = InfoBarSeverity.Informational;
                ImportExportInfoBar.Message = "No stations were imported.";
            }

            ImportExportInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ImportExportInfoBar.Severity = InfoBarSeverity.Error;
            ImportExportInfoBar.Message = $"Import failed: {ex.Message}";
            ImportExportInfoBar.IsOpen = true;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = GetActiveWindow();
            bool exported = await ViewModel.ExportStationsAsync(hwnd);

            if (exported)
            {
                ImportExportInfoBar.Severity = InfoBarSeverity.Success;
                ImportExportInfoBar.Message = "Stations exported successfully.";
            }
            else
            {
                ImportExportInfoBar.Severity = InfoBarSeverity.Informational;
                ImportExportInfoBar.Message = "Export cancelled or no stations to export.";
            }

            ImportExportInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ImportExportInfoBar.Severity = InfoBarSeverity.Error;
            ImportExportInfoBar.Message = $"Export failed: {ex.Message}";
            ImportExportInfoBar.IsOpen = true;
        }
    }
}
