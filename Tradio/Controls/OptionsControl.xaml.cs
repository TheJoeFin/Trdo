using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using Tradio.ViewModels;
using Windows.ApplicationModel;

namespace Tradio.Controls;

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
        // Try to get ViewModel from DataContext (set by parent page)
        if (DataContext is PlayerViewModel vm)
        {
            _vm = vm;
        }
        else
        {
            // Fallback: create our own instance if no DataContext provided
            _vm = new PlayerViewModel();
        }

        InitializeWithViewModel();
    }

    private void InitializeWithViewModel()
    {
        if (_vm is null) return;

        UpdatePlayPauseButton();
        VolumeSlider.Value = _vm.Volume;
        VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();

        StreamUrlTextBox.Text = _vm.StreamUrl; // init
        ValidateUrlAndUpdateUi(_vm.StreamUrl);

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
                if (StreamUrlTextBox.Text != _vm.StreamUrl)
                    StreamUrlTextBox.Text = _vm.StreamUrl;
                ValidateUrlAndUpdateUi(_vm.StreamUrl);
            }
            else if (args.PropertyName == nameof(PlayerViewModel.WatchdogStatus))
            {
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

    private void StreamUrlTextBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.StreamUrl = StreamUrlTextBox.Text?.Trim() ?? string.Empty;
        ValidateUrlAndUpdateUi(StreamUrlTextBox.Text?.Trim());
    }

    private void ApplyUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.ApplyStreamUrl() == true)
        {
            UrlErrorText.Text = string.Empty;
        }
        else
        {
            UrlErrorText.Text = "Please enter a valid http/https URL.";
        }
    }

    private void ValidateUrlAndUpdateUi(string? url)
    {
        bool valid = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        ApplyUrlButton.IsEnabled = valid;
        UrlErrorText.Text = valid ? string.Empty : "Enter a valid http/https URL.";
    }

    private async System.Threading.Tasks.Task InitializeStartupToggleAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync("TradioStartup");
            _initDone = true;
            UpdateStartupToggleFromState();
        }
        catch
        {
            // Could not get StartupTask (likely unpackaged). Disable toggle.
            StartupToggle.IsEnabled = false;
            StartupToggle.IsOn = false;
        }
    }

    private void UpdateStartupToggleFromState()
    {
        if (_startupTask is null) return;
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
        if (!_initDone || _startupTask is null) return;

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
