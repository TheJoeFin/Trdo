using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Pages;
using Trdo.ViewModels;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace Trdo;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private readonly PlayerViewModel _playerVm = PlayerViewModel.Shared;
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _trayIconRestoreEvent;
    private DispatcherQueueTimer? _trayIconWatchdogTimer;
    private DispatcherQueueTimer? _restoreEventMonitorTimer;

    public App()
    {
        InitializeComponent();
        _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;

        // Subscribe to theme change events
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Check for single instance using a named mutex
        const string mutexName = "Global\\Trdo_SingleInstance_Mutex";
        const string restoreEventName = "Global\\Trdo_RestoreTrayIcon_Event";

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                // Signal it to restore the tray icon before exiting
                try
                {
                    using EventWaitHandle restoreEvent = EventWaitHandle.OpenExisting(restoreEventName);
                    restoreEvent.Set();
                }
                catch
                {
                    // Event handle doesn't exist or couldn't be opened
                    // This is acceptable - the watchdog timer will eventually restore the icon
                }

                // Exit this instance gracefully
                Exit();
                return;
            }
        }
        catch (Exception)
        {
            // If mutex creation fails, allow the app to continue
            // This could happen in restricted environments
        }

        // Create the event handle for other instances to signal us
        try
        {
            _trayIconRestoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, restoreEventName);
        }
        catch
        {
            // If we can't create the event handle, continue without it
            // The watchdog timer will still provide periodic restoration
        }

        InitializeTrayIcon();
        await UpdateTrayIconAsync();
        UpdatePlayPauseCommandText();
        StartRestoreEventMonitor();
    }

    private void PlayerVmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            UpdatePlayPauseCommandText();
            // Update tray icon to reflect play/pause state
            _ = UpdateTrayIconAsync();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.IsBuffering))
        {
            // Update tray icon to show loading state
            _ = UpdateTrayIconAsync();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.CanPlay))
        {
            UpdatePlayPauseCommandText();
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        // Theme has changed, update the tray icon
        _ = UpdateTrayIconAsync();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = null;
        _trayIcon = new(0, "Assets/Radio.ico", "Trdo");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateFlyout();
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {

        Window window = new();
        window.Content = new ShellPage();
        window.Show();

        return;

        // Check if we can play (have stations available and one selected)
        if (!_playerVm.CanPlay)
        {
            // No stations available, show the flyout to encourage user to add a station
            args.Flyout = CreateFlyout();
            return;
        }

        // We have stations, toggle play/pause
        _playerVm.Toggle();
        _ = UpdateTrayIconAsync();
    }

    private Flyout CreateFlyout()
    {
        Flyout flyout = new()
        {
            Content = new ShellPage()
        };

        flyout.Closing += (s, e) =>
        {
            if (s is Flyout f)
                f.Content = null;
        };

        flyout.Opened += (s, e) =>
        {
            // Clear the back stack when flyout opens to prevent accumulation
            Services.NavigationService.Instance.ClearBackStack();
        };

        return flyout;
    }

    private async Task UpdateTrayIconAsync()
    {
        if (_trayIcon is null)
            return;

        // Detect system theme (true = dark theme, false = light theme)
        bool isDarkTheme = IsSystemInDarkMode();

        // Choose icon based on buffering, theme, and play state
        string iconUri;

        if (_playerVm.IsBuffering)
        {
            // When buffering/loading, use the hourglass icon
            iconUri = "Assets/Hourglass.ico";
        }
        else if (_playerVm.IsPlaying)
        {
            // When playing, use the regular Radio icon
            iconUri = "Assets/Radio.ico";
        }
        else
        {
            // When not playing, use theme-aware icons
            iconUri = isDarkTheme ? "Assets/Radio-White.ico" : "Assets/Radio-Black.ico";
        }

        try
        {
            _trayIcon.SetIcon(iconUri);
        }
        catch
        {
            // If the theme-specific icon doesn't exist, fallback to default Radio.ico
            _trayIcon.SetIcon("Assets/Radio.ico");
        }

        await Task.CompletedTask;
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            UISettings uiSettings = new();
            Color foregroundColor = uiSettings.GetColorValue(UIColorType.Foreground);

            // In dark mode, foreground color is light (high RGB values)
            // In light mode, foreground color is dark (low RGB values)
            return (foregroundColor.R + foregroundColor.G + foregroundColor.B) > 384;
        }
        catch
        {
            // Default to dark theme if detection fails
            return true;
        }
    }

    private void UpdatePlayPauseCommandText()
    {
        if (_trayIcon is null)
            return;

        if (!_playerVm.CanPlay)
        {
            _trayIcon.Tooltip = "Trdo - Add a station to start listening";
        }
        else if (_playerVm.IsPlaying)
        {
            _trayIcon.Tooltip = "Trdo (Playing) - Click to Pause";
        }
        else
        {
            _trayIcon.Tooltip = "Trdo - Play";
        }
    }

    private void StartRestoreEventMonitor()
    {
        // Only start monitoring if the event handle was created successfully
        if (_trayIconRestoreEvent is null)
            return;

        // Get the dispatcher queue for the current thread
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        // Create a timer that checks for restore signals frequently (every 2 seconds)
        _restoreEventMonitorTimer = dispatcherQueue.CreateTimer();
        _restoreEventMonitorTimer.Interval = TimeSpan.FromSeconds(2);
        _restoreEventMonitorTimer.Tick += async (sender, args) =>
        {
            try
            {
                // Check if the event was signaled without blocking
                if (_trayIconRestoreEvent?.WaitOne(0) == true)
                {
                    // Another instance requested tray icon restoration
                    await EnsureTrayIconVisibleAsync();
                }
            }
            catch
            {
                // Ignore any errors checking the event
            }
        };
        _restoreEventMonitorTimer.Start();
    }

    private async Task EnsureTrayIconVisibleAsync()
    {
        try
        {
            InitializeTrayIcon();
            await UpdateTrayIconAsync();
            UpdatePlayPauseCommandText();
        }
        catch
        {
            // Silent failure
        }
    }

    /// <summary>
    /// Cleanup resources when the application exits
    /// </summary>
    ~App()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _trayIconRestoreEvent?.Dispose();
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
