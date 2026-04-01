using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.Services;
using Trdo.ViewModels;

namespace Trdo.Pages;

public sealed partial class SettingsPage : Page
{
    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    private float _displayLevel;
    private bool _isUpdatingAutoPlayToggle;

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RadioPlayerService.Instance.Watchdog.AudioLevelUpdated += OnAudioLevelUpdated;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RadioPlayerService.Instance.Watchdog.AudioLevelUpdated -= OnAudioLevelUpdated;
    }

    private void OnAudioLevelUpdated(float rms)
    {
        // Marshal to UI thread — the event fires on the NAudio capture thread
        DispatcherQueue.TryEnqueue(() => UpdateLevelBars(rms));
    }

    private void UpdateLevelBars(float rms)
    {
        // Logarithmic (dB) normalisation  –  maps silence..full-scale to 0..1
        // Using a 50 dB range gives more visible movement in the mid-levels
        float db = rms > 0 ? 20f * MathF.Log10(rms) : -50f;
        float normalized = Math.Clamp((db + 50f) / 50f, 0f, 1f);

        // Fast attack, quick decay so bars visibly bounce with every beat
        const float decayFactor = 0.55f;
        _displayLevel = normalized > _displayLevel
            ? normalized
            : _displayLevel * (1f - decayFactor) + normalized * decayFactor;

        // Each bar has a threshold; opacity ramps proportionally above it
        // so even small fluctuations produce visible movement.
        const float dimOpacity = 0.12f;
        Bar1.Opacity = BarOpacity(_displayLevel, 0.05f, dimOpacity);
        Bar2.Opacity = BarOpacity(_displayLevel, 0.22f, dimOpacity);
        Bar3.Opacity = BarOpacity(_displayLevel, 0.42f, dimOpacity);
        Bar4.Opacity = BarOpacity(_displayLevel, 0.62f, dimOpacity);
        Bar5.Opacity = BarOpacity(_displayLevel, 0.80f, dimOpacity);
    }

    /// <summary>
    /// Returns a proportional opacity for a single bar.
    /// Below <paramref name="threshold"/> the bar is dim; above it the bar ramps
    /// smoothly from dim → fully lit over a short range so small level changes
    /// are clearly visible.
    /// </summary>
    private static double BarOpacity(float level, float threshold, float dim)
    {
        if (level <= threshold) return dim;
        // Ramp from dim → 1.0 over 0.15 of level range above the threshold
        float t = Math.Min((level - threshold) / 0.15f, 1f);
        return dim + t * (1.0 - dim);
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

    private void AutoPlayOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingAutoPlayToggle)
        {
            return;
        }

        bool isEnabled = ViewModel.IsAutoPlayOnStartupEnabled;
        bool requestedState = AutoPlayOnStartupToggle.IsOn;

        if (requestedState == isEnabled)
        {
            if (!requestedState)
            {
                AutoPlayWarningInfoBar.IsOpen = false;
            }

            return;
        }

        if (!requestedState)
        {
            ViewModel.IsAutoPlayOnStartupEnabled = false;
            AutoPlayWarningInfoBar.IsOpen = false;
            return;
        }

        SetAutoPlayToggle(false);
        AutoPlayWarningInfoBar.IsOpen = true;
    }

    private void ConfirmAutoPlayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsAutoPlayOnStartupEnabled = true;
        SetAutoPlayToggle(true);
        AutoPlayWarningInfoBar.IsOpen = false;
    }

    private void SetAutoPlayToggle(bool value)
    {
        _isUpdatingAutoPlayToggle = true;
        AutoPlayOnStartupToggle.IsOn = value;
        _isUpdatingAutoPlayToggle = false;
    }
}
