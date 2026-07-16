using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Controls;
using Trdo.Pages;
using Trdo.Services;
using Trdo.Services.Playback;
using Trdo.ViewModels;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace Trdo;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private TrayPopupWindow? _trayPopupWindow;
    private MiniPlayerWindow? _miniPlayerWindow;
    private readonly PlayerViewModel _playerVm = PlayerViewModel.Shared;
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _trayIconRestoreEvent;
    private TaskbarCreatedMonitor? _taskbarCreatedMonitor;
    private DispatcherQueue? _uiDispatcherQueue;
    private DispatcherQueueTimer? _restoreEventMonitorTimer;

    /// <summary>
    /// Maximum length for the now playing text in the tooltip before truncation.
    /// </summary>
    private const int MaxTooltipNowPlayingLength = 60;

    /// <summary>
    /// Maximum length for the full tray icon tooltip (NOTIFYICONDATA limit).
    /// </summary>
    private const int MaxTrayTooltipLength = 128;

    public App()
    {
        InitializeComponent();
        _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;

        // Initialize PlaylistHistoryService early so it captures metadata from the start
        PlaylistHistoryService.EnsureInitialized();

        // Subscribe to theme change events
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public void TryShowFlyout()
    {
        WindowPlacementService.CapturePointerAnchor();
        ShowTrayPopup();
    }

    public void ShowMiniPlayerWindow()
    {
        WindowPlacementService.CapturePointerAnchor();

        if (_miniPlayerWindow is null)
        {
            _miniPlayerWindow = new MiniPlayerWindow();
            WindowHelper.Track(_miniPlayerWindow);
            _miniPlayerWindow.Closed += (_, _) => _miniPlayerWindow = null;
        }

        // Position before activating so the window never flashes at a stale location.
        WindowPlacementService.PositionWindowNearAnchor(_miniPlayerWindow, 320, 220);
        _miniPlayerWindow.Activate();
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

        try
        {
            _taskbarCreatedMonitor = new TaskbarCreatedMonitor();
            _taskbarCreatedMonitor.TaskbarCreated += OnTaskbarCreated;
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"[App] Failed to register TaskbarCreated monitor: {ex.Message}");
        }

        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        InitializeTrayIcon();
        await UpdateTrayIconAsync();
        UpdatePlayPauseCommandText(forceTooltip: true);
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
        else if (e.PropertyName == nameof(PlayerViewModel.NowPlaying) ||
                 e.PropertyName == nameof(PlayerViewModel.HasNowPlaying))
        {
            // Update tooltip when now playing info changes
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
        if (_trayIcon is not null)
            return;

        _trayIcon = new(0, "Assets/Radio.ico", "Trdo");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIconSource(_trayIcon);

        // Only show tutorial window on first run
        if (SettingsService.IsFirstRun)
        {
            TutorialWindow tutorialWindow = new();
            tutorialWindow.Show();
        }
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        if (SettingsService.TrayClickBehavior == 1)
        {
            // Swapped: right click plays/pauses (fall back to flyout if no station selected)
            if (_playerVm.CanPlay)
            {
                _playerVm.Toggle();
                _ = UpdateTrayIconAsync();
                return;
            }
        }

        // Default: right click opens flyout; also fallback when no station is available
        ShowFlyout(args);
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        if (SettingsService.TrayClickBehavior == 1)
        {
            // Swapped: left click opens flyout
            ShowFlyout(args);
            return;
        }

        // Default: left click plays/pauses
        // Check if we can play (have stations available and one selected)
        if (!_playerVm.CanPlay)
        {
            // No stations available, show the flyout to encourage user to add a station
            ShowFlyout(args);
            return;
        }

        // We have stations, toggle play/pause
        _playerVm.Toggle();
        _ = UpdateTrayIconAsync();
    }

    private void ShowFlyout(TrayIconEventArgs args)
    {
        WindowPlacementService.CapturePointerAnchor();
        ShowTrayPopup();
    }

    private void ShowTrayPopup()
    {
        if (_trayPopupWindow is null)
        {
            _trayPopupWindow = new TrayPopupWindow();
            WindowHelper.Track(_trayPopupWindow);
            _trayPopupWindow.Closed += (_, _) => _trayPopupWindow = null;
        }

        _trayPopupWindow.ToggleNearAnchor();
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

        // SetIcon can clear the native tooltip even when the text is unchanged.
        UpdatePlayPauseCommandText(forceTooltip: true);

        await Task.CompletedTask;
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            // Read the system (taskbar) theme, not the app theme.
            // SystemUsesLightTheme = 0 means dark taskbar, 1 means light taskbar.
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            if (value is int intVal)
                return intVal == 0;

            return true;
        }
        catch
        {
            // Default to dark theme if detection fails
            return true;
        }
    }

    private void UpdatePlayPauseCommandText(bool forceTooltip = false)
    {
        if (_trayIcon is null)
            return;

        string station = _playerVm.SelectedStation?.Name ?? string.Empty;
        station = station.Split(" ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        string tooltip;
        if (!_playerVm.CanPlay)
        {
            tooltip = "Trdo - Add a station to start listening";
        }
        else if (_playerVm.IsPlaying)
        {
            string playPauseClickHint = SettingsService.TrayClickBehavior == 1
                ? "Right-click to pause"
                : "Left-click to pause";

            if (_playerVm.HasNowPlaying)
            {
                string nowPlaying = _playerVm.NowPlaying;
                if (nowPlaying.Length > MaxTooltipNowPlayingLength)
                {
                    nowPlaying = string.Concat(nowPlaying.AsSpan(0, MaxTooltipNowPlayingLength - 3), "...");
                }

                tooltip = $"Trdo {station} (Playing)\n{nowPlaying}\n{playPauseClickHint}";
            }
            else
            {
                tooltip = $"Trdo {station} (Playing)\n{playPauseClickHint}";
            }
        }
        else
        {
            string playPauseClickHint = SettingsService.TrayClickBehavior == 1
                ? "Right-click to play"
                : "Left-click to play";

            tooltip = $"Trdo {station} (Paused)\n{playPauseClickHint}";
        }

        SetTrayTooltip(tooltip, forceTooltip);
    }

    private void SetTrayTooltip(string? text, bool force = false)
    {
        if (_trayIcon is null)
            return;

        string tooltip = string.IsNullOrWhiteSpace(text) ? "Trdo" : text.Trim();
        if (tooltip.Length > MaxTrayTooltipLength)
        {
            tooltip = string.Concat(tooltip.AsSpan(0, MaxTrayTooltipLength - 3), "...");
        }

        void Apply()
        {
            if (force)
            {
                // WinUIEx only sends a native tooltip update when the value changes.
                _trayIcon.Tooltip = "\u200B";
            }

            _trayIcon.Tooltip = tooltip;
        }

        if (_uiDispatcherQueue is not null && !_uiDispatcherQueue.HasThreadAccess)
        {
            _uiDispatcherQueue.TryEnqueue(Apply);
            return;
        }

        Apply();
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

    private async void OnTaskbarCreated(object? sender, EventArgs e)
    {
        await EnsureTrayIconVisibleAsync();
    }

    private async Task EnsureTrayIconVisibleAsync()
    {
        try
        {
            if (_trayIcon is null)
            {
                InitializeTrayIcon();
            }
            else
            {
                _trayPopupWindow?.HidePopup();
                _trayIcon.IsVisible = false;
                _trayIcon.IsVisible = true;
            }

            await UpdateTrayIconAsync();
            UpdatePlayPauseCommandText(forceTooltip: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Failed to recreate tray icon: {ex}");
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
            LibVlcHost.Dispose();
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
