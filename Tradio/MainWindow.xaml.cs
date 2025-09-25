using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Tradio.ViewModels;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel;

namespace Tradio
{
    public sealed partial class MainWindow : Window
    {
        private readonly PlayerViewModel _vm = new();
        private bool _initDone;
        private StartupTask? _startupTask;

        public MainWindow()
        {
            InitializeComponent();
            WindowHelper.Track(this);

            // Intercept window closing to just hide instead of exiting the app
            try
            {
                AppWindow.Closing += OnAppWindowClosing;
            }
            catch { /* AppWindow may not be available in some environments */ }

            UpdatePlayPauseButton();
            VolumeSlider.Value = _vm.Volume;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();

            StreamUrlTextBox.Text = _vm.StreamUrl; // init
            ValidateUrlAndUpdateUi(_vm.StreamUrl);

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
            };

            _ = InitializeStartupToggleAsync();
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // Cancel the close and just hide the window so playback continues from tray
            args.Cancel = true;
            try { sender.Hide(); } catch { }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Toggle();
            UpdatePlayPauseButton();
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _vm.Volume = e.NewValue;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
        }

        private void UpdatePlayPauseButton()
        {
            if (PlayPauseButton is not null)
            {
                PlayPauseButton.Content = _vm.IsPlaying ? "Pause" : "Play";
            }
        }

        private void StreamUrlTextBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            _vm.StreamUrl = StreamUrlTextBox.Text?.Trim() ?? string.Empty;
            ValidateUrlAndUpdateUi(_vm.StreamUrl);
        }

        private void ApplyUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ApplyStreamUrl())
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
                            var result = await _startupTask.RequestEnableAsync();
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
    }
}
