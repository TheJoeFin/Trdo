using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using Trdo.ViewModels;
using Windows.ApplicationModel;

namespace Trdo.Controls;

public sealed partial class OptionsControl : UserControl
{
    private PlayerViewModel? _vm;
    private bool _initDone;
    private StartupTask? _startupTask;

    public OptionsControl()
    {
        InitializeComponent();
        Loaded += OptionsControl_Loaded;
    }

    private void OptionsControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Use the shared PlayerViewModel instance
        _vm = PlayerViewModel.Shared;
        InitializeWithViewModel();
    }

    private void InitializeWithViewModel()
    {
        if (_vm is null) return;

        UpdatePlayPauseButton();
        VolumeSlider.Value = _vm.Volume;
        VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();

        // Display current stream URL (read-only display)
        if (StreamUrlTextBox != null)
        {
            StreamUrlTextBox.Text = _vm.StreamUrl;
            StreamUrlTextBox.IsReadOnly = true; // Stations manage URLs now
        }

        // Initialize watchdog toggle
        WatchdogToggle.IsOn = _vm.WatchdogEnabled;

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PlayerViewModel.IsPlaying))
            {
                UpdatePlayPauseButton();
            }
            else if (args.PropertyName == nameof(PlayerViewModel.Volume))
            {
                if (Math.Abs(VolumeSlider.Value - _vm.Volume) > 0.0001)
                    VolumeSlider.Value = _vm.Volume;
                VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
            }
            else if (args.PropertyName == nameof(PlayerViewModel.StreamUrl))
            {
                if (StreamUrlTextBox != null && StreamUrlTextBox.Text != _vm.StreamUrl)
                    StreamUrlTextBox.Text = _vm.StreamUrl;
            }
            else if (args.PropertyName == nameof(PlayerViewModel.WatchdogStatus))
            {
                if (WatchdogStatusText != null)
                    WatchdogStatusText.Text = _vm.WatchdogStatus;
            }
            else if (args.PropertyName == nameof(PlayerViewModel.WatchdogEnabled))
            {
                if (WatchdogToggle.IsOn != _vm.WatchdogEnabled)
                    WatchdogToggle.IsOn = _vm.WatchdogEnabled;
            }
        };

        _ = InitializeStartupToggleAsync();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _vm?.Toggle();
        UpdatePlayPauseButton();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.Volume = e.NewValue;
        VolumeValue.Text = ((int)(e.NewValue * 100)).ToString();
    }

    private void UpdatePlayPauseButton()
    {
        if (PlayPauseButton is not null && _vm is not null)
        {
            PlayPauseButton.Content = _vm.IsPlaying ? "⏸️ Pause" : "▶️ Play";
        }
    }

    private async System.Threading.Tasks.Task InitializeStartupToggleAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync("TrdoStartup");
            _initDone = true;
            UpdateStartupToggleFromState();
        }
        catch
        {
            // Could not get StartupTask (likely unpackaged). Disable toggle.
            if (StartupToggle != null)
            {
                StartupToggle.IsEnabled = false;
                StartupToggle.IsOn = false;
            }
        }
    }

    private void UpdateStartupToggleFromState()
    {
        if (_startupTask is null || StartupToggle is null) return;
        switch (_startupTask.State)
        {
            case StartupTaskState.Enabled:
                StartupToggle.IsEnabled = true;
                StartupToggle.IsOn = true;
                StartupToggle.Header = "Start with Windows";
                break;
            case StartupTaskState.Disabled:
                StartupToggle.IsEnabled = true;
                StartupToggle.IsOn = false;
                StartupToggle.Header = "Start with Windows";
                break;
            case StartupTaskState.DisabledByUser:
                StartupToggle.IsEnabled = false;
                StartupToggle.IsOn = false;
                StartupToggle.Header = "Start with Windows (disabled in Settings)";
                break;
            case StartupTaskState.DisabledByPolicy:
            default:
                StartupToggle.IsEnabled = false;
                StartupToggle.IsOn = false;
                StartupToggle.Header = "Start with Windows (disabled by policy)";
                break;
        }
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initDone || _startupTask is null || StartupToggle is null) return;

        try
        {
            if (StartupToggle.IsOn)
            {
                switch (_startupTask.State)
                {
                    case StartupTaskState.Disabled:
                        StartupTaskState result = await _startupTask.RequestEnableAsync();
                        break;
                    case StartupTaskState.DisabledByUser:
                        // no-op: cannot enable programmatically
                        break;
                }
            }
            else
            {
                if (_startupTask.State == StartupTaskState.Enabled)
                {
                    _startupTask.Disable();
                }
            }
        }
        catch
        {
            // ignore errors
        }

        // Reflect actual state after operation
        UpdateStartupToggleFromState();
    }

    private void WatchdogToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            _vm.WatchdogEnabled = WatchdogToggle.IsOn;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Exit();
    }
}
