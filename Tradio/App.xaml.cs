using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Threading.Tasks;
using Tradio.Controls;
using Tradio.ViewModels;
using WinUIEx;

namespace Tradio
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private readonly PlayerViewModel _playerVm = new();

        public App()
        {
            InitializeComponent();
            _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            InitializeTrayIcon();
            await UpdateTrayIconAsync();
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
            _window = new Window
            {
                Title = "Tradio"
            };
            WindowManager wm = WindowManager.Get(_window);
            wm.TrayIconInvoked += Wm_TrayIconInvoked;
            _window.AppWindow.SetTaskbarIcon("Assets/Radio.ico");
            wm.IsVisibleInTray = true; // Show app in tray
                                       // Minimize to tray:
            wm.WindowStateChanged += (s, state) =>
                wm.AppWindow.IsShownInSwitchers = state != WindowState.Minimized;

            wm.WindowState = WindowState.Minimized; // Delay activating the window by starting minimized
        }

        private void Wm_TrayIconInvoked(object? sender, TrayIconInvokedEventArgs e)
        {
            if (e.Type == TrayIconInvokeType.RightMouseUp)
            {
                Flyout flyout = new()
                {
                    Content = new OptionsControl()
                };
                e.Flyout = flyout;
            }
            else if (e.Type == TrayIconInvokeType.LeftMouseUp)
            {
                _playerVm.Toggle();
                _ = UpdateTrayIconAsync();
            }
        }

        private async Task UpdateTrayIconAsync()
        {
            if (_window is null)
                return;

            // Choose icon based on play state. When not playing, use Radio-Off.png
            // Fallback to Radio.ico when playing or if the PNG isn't present.
            string iconUri = _playerVm.IsPlaying
                ? "Assets/Radio.ico"
                : "Assets/Radio-Off.ico";

            // TODO: maybe make this a little more robust witha try/catch
            // _window.SetTaskBarIcon(Icon.FromFile(iconUri));
            _window.AppWindow.SetTaskbarIcon(iconUri);
        }

        private void UpdatePlayPauseCommandText()
        {
            if (this._window is null)
                return;

            if (_playerVm.IsPlaying)
            {
                _window.Title = "Tradio - Pause";
            }
            else
            {
                _window.Title = "Tradio - Play";
            }
        }
    }
}
