using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Tradio.ViewModels;
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

        public App()
        {
            InitializeComponent();
            _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            InitializeTrayIcon();
            await UpdateTrayIconAsync();
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
            _trayIcon.ForceCreate();
        }

        private async Task UpdateTrayIconAsync()
        {
            // Try radio.ico; if missing, don't set to avoid invalid PNG icon exception
            try
            {
                var preferred = new Uri("ms-appx:///Assets/radio.ico");
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
                var appWin = _window.AppWindow;
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
    }
}
