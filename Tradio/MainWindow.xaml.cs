using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Trdo.ViewModels;
using Windows.ApplicationModel;
using Windows.Graphics;

namespace Trdo
{
    public sealed partial class MainWindow : Window
    {
        private readonly PlayerViewModel _vm = PlayerViewModel.Shared; // Use shared instance
        private bool _initDone;
        private StartupTask? _startupTask;

        public MainWindow()
        {
            Debug.WriteLine("=== MainWindow Constructor START ===");

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

            Debug.WriteLine($"[MainWindow] Initial ViewModel state - IsPlaying: {_vm.IsPlaying}, Volume: {_vm.Volume}");
            Debug.WriteLine($"[MainWindow] Initial selected station: {_vm.SelectedStation?.Name ?? "null"}");
            Debug.WriteLine($"[MainWindow] Initial stream URL: {_vm.StreamUrl}");

            UpdatePlayPauseButton();
            VolumeSlider.Value = _vm.Volume;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();

            // Display current stream URL (read-only display)
            if (StreamUrlTextBox != null)
            {
                StreamUrlTextBox.Text = _vm.StreamUrl;
                StreamUrlTextBox.IsReadOnly = true; // Make it read-only as stations manage URLs now
            }

            // Initialize watchdog toggle
            WatchdogToggle.IsOn = _vm.WatchdogEnabled;
            Debug.WriteLine($"[MainWindow] Watchdog enabled: {_vm.WatchdogEnabled}");

            _vm.PropertyChanged += (_, args) =>
            {
                Debug.WriteLine($"[MainWindow] ViewModel PropertyChanged: {args.PropertyName}");

                if (args.PropertyName == nameof(PlayerViewModel.IsPlaying))
                {
                    Debug.WriteLine($"[MainWindow] IsPlaying changed to: {_vm.IsPlaying}");
                    UpdatePlayPauseButton();
                }
                else if (args.PropertyName == nameof(PlayerViewModel.Volume))
                {
                    if (Math.Abs(VolumeSlider.Value - _vm.Volume) > 0.0001)
                    {
                        Debug.WriteLine($"[MainWindow] Volume changed to: {_vm.Volume}");
                        VolumeSlider.Value = _vm.Volume;
                    }
                    VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
                }
                else if (args.PropertyName == nameof(PlayerViewModel.StreamUrl))
                {
                    Debug.WriteLine($"[MainWindow] StreamUrl changed to: {_vm.StreamUrl}");
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
                    Debug.WriteLine($"[MainWindow] WatchdogEnabled changed to: {_vm.WatchdogEnabled}");
                    if (WatchdogToggle.IsOn != _vm.WatchdogEnabled)
                        WatchdogToggle.IsOn = _vm.WatchdogEnabled;
                }
            };

            _ = InitializeStartupToggleAsync();

            Debug.WriteLine("=== MainWindow Constructor END ===");
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
            Debug.WriteLine("=== PlayPauseButton_Click (MainWindow) START ===");
            Debug.WriteLine($"[MainWindow] Current IsPlaying: {_vm.IsPlaying}");
            Debug.WriteLine($"[MainWindow] Current selected station: {_vm.SelectedStation?.Name ?? "null"}");
            Debug.WriteLine($"[MainWindow] Current stream URL: {_vm.StreamUrl}");

            _vm.Toggle();
            Debug.WriteLine($"[MainWindow] After Toggle - IsPlaying: {_vm.IsPlaying}");

            UpdatePlayPauseButton();

            Debug.WriteLine("=== PlayPauseButton_Click (MainWindow) END ===");
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Debug.WriteLine($"[MainWindow] Volume slider changed to: {e.NewValue}");
            _vm.Volume = e.NewValue;
            VolumeValue.Text = ((int)(_vm.Volume * 100)).ToString();
        }

        private void UpdatePlayPauseButton()
        {
            if (PlayPauseButton is not null)
            {
                string newContent = _vm.IsPlaying ? "Pause" : "Play";
                Debug.WriteLine($"[MainWindow] Updating PlayPauseButton to: {newContent}");
                PlayPauseButton.Content = newContent;
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
            Debug.WriteLine($"[MainWindow] WatchdogToggle toggled to: {WatchdogToggle.IsOn}");
            _vm.WatchdogEnabled = WatchdogToggle.IsOn;
        }
    }
}
