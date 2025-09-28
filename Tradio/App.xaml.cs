using H.NotifyIcon;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Tradio.ViewModels;
using Windows.ApplicationModel;
using Windows.Storage;

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

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
                // Update tray icon to reflect play/pause state
                _ = UpdateTrayIconAsync();
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
            if (_trayIcon is null) return;

            // Choose icon based on play state. When not playing, use Radio-Off.png
            // Fallback to Radio.ico when playing or if the PNG isn't present.
            string iconUri = _playerVm.IsPlaying
                ? "ms-appx:///Assets/Radio.ico"
                : "ms-appx:///Assets/Radio-Off.ico";

            try
            {
                Uri preferred = new(iconUri);
                _ = await StorageFile.GetFileFromApplicationUriAsync(preferred);
                _trayIcon.IconSource = new BitmapImage(preferred);
            }
            catch
            {
                // Fallback: try the default Radio.ico if available
                try
                {
                    Uri fallback = new("ms-appx:///Assets/Radio.ico");
                    _ = await StorageFile.GetFileFromApplicationUriAsync(fallback);
                    _trayIcon.IconSource = new BitmapImage(fallback);
                }
                catch
                {
                    // No valid icon found; keep existing icon
                }
            }
        }

        private void UpdatePlayPauseCommandText()
        {
            if (_playPauseCommand is null) return;
            _playPauseCommand.Label = _playerVm.IsPlaying ? "Pause" : "Play";

            // Update the icon as well
            FontIconSource? iconSource = _playPauseCommand.IconSource as FontIconSource;
            if (iconSource != null)
            {
                // Play icon: &#xE768; (Play), Pause icon: &#xE769; (Pause)
                iconSource.Glyph = _playerVm.IsPlaying ? "\uE769" : "\uE768";
            }
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
            _ = UpdateTrayIconAsync();
        }
    }
}
