using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Tradio.ViewModels;
using Windows.Storage;
using Windows.ApplicationModel;
using Microsoft.UI.Input;

namespace Tradio
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private readonly PlayerViewModel _playerVm = new();
        private TaskbarIcon? _trayIcon;
        private XamlUICommand? _showHideCommand;
        private XamlUICommand? _exitCommand;
        private XamlUICommand? _playPauseCommand;
        private XamlUICommand? _toggleStartupCommand;

        public App()
        {
            InitializeComponent();
            _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            InitializeTrayIcon();
            await UpdateTrayIconAsync();
            await UpdateStartupCommandLabelAsync();
            UpdatePlayPauseCommandText();
        }

        private void PlayerVmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
            {
                UpdatePlayPauseCommandText();
            }
        }

        private void InitializeTrayIcon()
        {
            _showHideCommand = (XamlUICommand)Resources["ShowHideWindowCommand"];
            _showHideCommand.ExecuteRequested += ShowHideWindowCommand_ExecuteRequested;

            _exitCommand = (XamlUICommand)Resources["ExitApplicationCommand"];
            _exitCommand.ExecuteRequested += ExitApplicationCommand_ExecuteRequested;

            _playPauseCommand = (XamlUICommand)Resources["PlayPauseCommand"];
            _playPauseCommand.ExecuteRequested += PlayPauseCommand_ExecuteRequested;

            _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
            
            // Doesn't work for some reason
            // Adjust volume with the mouse wheel over the tray icon via PointerWheelChanged.
            //_trayIcon.PointerWheelChanged += TrayIcon_PointerWheelChanged;


            _trayIcon.ForceCreate();
        }

        private void TrayIcon_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                // Use PointerPoint to get wheel delta. Positive delta => wheel up.
                PointerPoint point = e.GetCurrentPoint(_trayIcon);
                int delta = point.Properties.MouseWheelDelta;

                if (delta == 0)
                    return;

                const double step = 0.05; // 5% per notch
                double newVol = _playerVm.Volume + (delta > 0 ? step : -step);
                _playerVm.Volume = Math.Clamp(newVol, 0, 1);

                e.Handled = true;
            }
            catch
            {
                // ignore
            }
        }

        private async Task UpdateTrayIconAsync()
        {
            // Try radio.ico; if missing, don't set to avoid invalid PNG icon exception
            try
            {
                Uri preferred = new Uri("ms-appx:///Assets/radio.ico");
                _ = await StorageFile.GetFileFromApplicationUriAsync(preferred);
                _trayIcon!.IconSource = new BitmapImage(preferred);
            }
            catch
            {
                // No valid .ico found; TaskbarIcon will keep its default shell icon
            }
        }

        private void UpdatePlayPauseCommandText()
        {
            if (_playPauseCommand is null) return;
            _playPauseCommand.Label = _playerVm.IsPlaying ? "Pause" : "Play";
        }

        private async Task UpdateStartupCommandLabelAsync()
        {
            if (_toggleStartupCommand is null) return;
            try
            {
                StartupTask task = await StartupTask.GetAsync("TradioStartup");
                _toggleStartupCommand.Label = task.State == StartupTaskState.Enabled ? "Disable Start with Windows" : "Enable Start with Windows";
            }
            catch
            {
                _toggleStartupCommand.Label = "Enable Start with Windows";
            }
        }

        private void ShowHideWindowCommand_ExecuteRequested(object? _, ExecuteRequestedEventArgs args)
        {
            if (_window == null)
            {
                _window = new MainWindow();
                _window.Closed += (sender, e) => { _window = null; };
                _window.Activate();
                return;
            }

            try
            {
                AppWindow? appWin = _window.AppWindow;
                if (appWin is not null)
                {
                    if (appWin.IsVisible)
                        appWin.Hide();
                    else
                    {
                        appWin.Show();
                        _window.Activate();
                    }
                }
                else
                {
                    _window.Activate();
                }
            }
            catch
            {
                _window.Activate();
            }
        }

        private void ExitApplicationCommand_ExecuteRequested(object? _, ExecuteRequestedEventArgs args)
        {
            _trayIcon?.Dispose();
            Exit();
        }

        private void PlayPauseCommand_ExecuteRequested(object? _, ExecuteRequestedEventArgs args)
        {
            _playerVm.Toggle();
            UpdatePlayPauseCommandText();
        }

        private async void ToggleStartupCommand_ExecuteRequested(object? sender, ExecuteRequestedEventArgs e)
        {
            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TradioStartup");
                switch (startupTask.State)
                {
                    case StartupTaskState.Disabled:
                        StartupTaskState newState = await startupTask.RequestEnableAsync();
                        break;
                    case StartupTaskState.DisabledByUser:
                        // Cannot enable programmatically; show info
                        break;
                    case StartupTaskState.Enabled:
                        startupTask.Disable();
                        break;
                    case StartupTaskState.DisabledByPolicy:
                    default:
                        break;
                }
            }
            catch
            {
                // ignore
            }
            await UpdateStartupCommandLabelAsync();
        }
    }
}
