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
    private readonly PlayerViewModel _playerVm = new();
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private DispatcherQueueTimer? _trayIconWatchdogTimer;
    private ShellPage? _shellPage;

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

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running
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

        InitializeTrayIcon();
        await UpdateTrayIconAsync();
        StartTrayIconWatchdog();
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

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        // Theme has changed, update the tray icon
        _ = UpdateTrayIconAsync();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new(0, "Assets/Radio.ico", "Trdo");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        _shellPage = new();
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        Flyout flyout = new()
        {
            Content = _shellPage
        };

        flyout.Closing += (s, e) =>
        {
            if (s is Flyout f)
                f.Content = null;
        };

        args.Flyout = flyout;
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        _playerVm.Toggle();
        _ = UpdateTrayIconAsync();
    }

    private async Task UpdateTrayIconAsync()
    {
        if (_trayIcon is null)
            return;

        // Detect system theme (true = dark theme, false = light theme)
        bool isDarkTheme = IsSystemInDarkMode();

        // Choose icon based on theme and play state
        string iconUri;
        if (_playerVm.IsPlaying)
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

        if (_playerVm.IsPlaying)
        {
            _trayIcon.Tooltip = "Trdo (Playing) - Click to Pause";
        }
        else
        {
            _trayIcon.Tooltip = "Trdo - Play";
        }
    }

    private void StartTrayIconWatchdog()
    {
        // Get the dispatcher queue for the current thread
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        // Create a timer that checks tray icon visibility every 10 seconds
        _trayIconWatchdogTimer = dispatcherQueue.CreateTimer();
        _trayIconWatchdogTimer.Interval = TimeSpan.FromSeconds(10);
        _trayIconWatchdogTimer.Tick += async (sender, args) =>
        {
            await EnsureTrayIconVisibleAsync();
        };
        _trayIconWatchdogTimer.Start();
    }

    private async Task EnsureTrayIconVisibleAsync()
    {
        if (_trayIcon is null)
        {
            InitializeTrayIcon();
            return;
        }

        try
        {
            // Check if the tray icon is visible
            if (!_trayIcon.IsVisible)
            {
                // Tray icon disappeared, restore it
                _trayIcon.IsVisible = true;
                await UpdateTrayIconAsync();
                UpdatePlayPauseCommandText();
            }
        }
        catch
        {
            // If there's an error checking/restoring visibility, try to recreate the tray icon
            try
            {
                InitializeTrayIcon();
                await UpdateTrayIconAsync();
                UpdatePlayPauseCommandText();
            }
            catch
            {
                // Silent failure - will try again on next timer tick
            }
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
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
