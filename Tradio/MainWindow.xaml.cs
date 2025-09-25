using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Tradio.ViewModels;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel;
using Windows.Graphics;
using System.Runtime.InteropServices;

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

            // Use WinAppSDK TitleBar control as custom title bar
            try
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(SimpleTitleBar);
            }
            catch { }

            // Position window small at bottom-right on first show
            TryPositionBottomRightSmall();

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

        private void TryPositionBottomRightSmall()
        {
            try
            {
                AppWindow? appWin = this.AppWindow;
                if (appWin is null)
                    return;

                // Base size in DIPs; scale to monitor DPI to get pixels
                double scale = GetScaleForCurrentWindow();
                int width = (int)Math.Round(420 * scale);
                int height = (int)Math.Round(260 * scale);
                int margin = (int)Math.Round(12 * scale);

                // Use the display area for this window
                DisplayArea displayArea = DisplayArea.GetFromWindowId(appWin.Id, DisplayAreaFallback.Primary);
                RectInt32 workArea = displayArea.WorkArea; // work area accounts for taskbar

                // Ensure we don't exceed work area
                width = Math.Min(width, workArea.Width);
                height = Math.Min(height, workArea.Height);

                int x = workArea.X + Math.Max(0, workArea.Width - width - margin);
                int y = workArea.Y + Math.Max(0, workArea.Height - height - margin);

                RectInt32 rect = new(x, y, width, height);
                appWin.MoveAndResize(rect);

                if (appWin.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                    presenter.IsMinimizable = true;
                }
            }
            catch
            {
                // best-effort; ignore if positioning fails
            }
        }

        private static class NativeMethods
        {
            public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

            [DllImport("Shcore.dll")]
            public static extern int GetDpiForMonitor(IntPtr hmonitor, Monitor_DPI_Type dpiType, out uint dpiX, out uint dpiY);

            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(IntPtr hwnd);
        }

        private enum Monitor_DPI_Type
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2,
            MDT_DEFAULT = MDT_EFFECTIVE_DPI
        }

        private double GetScaleForCurrentWindow()
        {
            try
            {
                nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd != IntPtr.Zero)
                {
                    // Try monitor DPI first
                    IntPtr hmon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                    if (hmon != IntPtr.Zero)
                    {
                        if (NativeMethods.GetDpiForMonitor(hmon, Monitor_DPI_Type.MDT_EFFECTIVE_DPI, out uint dx, out uint _) == 0 && dx > 0)
                        {
                            return dx / 96.0;
                        }
                    }

                    // Fallback to window DPI
                    uint dpi = NativeMethods.GetDpiForWindow(hwnd);
                    if (dpi > 0)
                    {
                        return dpi / 96.0;
                    }
                }
            }
            catch
            {
                // ignore and use default scale
            }
            return 1.0;
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
    }
}
