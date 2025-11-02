using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Pages;
using Trdo.ViewModels;
using Trdo.Widgets;
using Trdo.Widgets.Helper;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace Trdo;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private readonly PlayerViewModel _playerVm = PlayerViewModel.Shared;
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private DispatcherQueueTimer? _trayIconWatchdogTimer;
    private DispatcherQueueTimer? _sharedStatePollingTimer;
    private ShellPage? _shellPage;
    private RegistrationManager<TrdoWidgetProvider>? _widgetRegistrationManager;
    private bool _isComServerMode = false;
    private bool _lastKnownPlayingState = false;

    public App()
    {
        InitializeComponent();
        _playerVm.PropertyChanged += PlayerVmOnPropertyChanged;

        // Subscribe to theme change events
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Check if launched to register COM server for widgets
        string[] cmdLineArgs = Environment.GetCommandLineArgs();
        if (cmdLineArgs.Contains("-RegisterProcessAsComServer"))
        {
            _isComServerMode = true;

            // Initialize COM wrappers for widget provider
            WinRT.ComWrappersSupport.InitializeComWrappers();
            _widgetRegistrationManager = RegistrationManager<TrdoWidgetProvider>.RegisterProvider();

            // Start shared state polling even in COM server mode
            // This ensures the widget process syncs its MediaPlayer with main app
            StartSharedStatePollingForComServer();

            // Keep the app running as a COM server
            // Widget provider will handle widget requests
            // Don't initialize tray icon or UI in COM server mode
            return;
        }

        // Normal app mode - check for single instance using a named mutex
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
        UpdatePlayPauseCommandText();
        StartTrayIconWatchdog();
        StartSharedStatePolling();
    }

    private void PlayerVmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            UpdatePlayPauseCommandText();
            // Update tray icon to reflect play/pause state
            _ = UpdateTrayIconAsync();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.CanPlay))
        {
            UpdatePlayPauseCommandText();
        }
        else if (e.PropertyName == nameof(PlayerViewModel.SelectedStation))
        {
            // Station changed, update tray icon tooltip
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
        _trayIcon = new(0, "Assets/Radio.ico", "Trdo");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        _shellPage = new();
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateFlyout();
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
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
            Content = _shellPage,
            AllowFocusOnInteraction = false
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

    private void StartSharedStatePolling()
    {
        // Get the dispatcher queue for the current thread
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        // Create a timer that polls shared state every 2 seconds
        // This ensures we detect changes from the widget process
        _sharedStatePollingTimer = dispatcherQueue.CreateTimer();
        _sharedStatePollingTimer.Interval = TimeSpan.FromSeconds(2);
        _sharedStatePollingTimer.Tick += (sender, args) =>
        {
            CheckSharedState();
        };
        _sharedStatePollingTimer.Start();
        
        // Initialize the last known state
        _lastKnownPlayingState = _playerVm.IsPlaying;
    }

    private void CheckSharedState()
    {
        try
        {
            // Get shared state (what should be happening)
            bool sharedIsPlaying = false;
            try
            {
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("RadioIsPlaying", out object? storedValue))
                {
                    sharedIsPlaying = storedValue is bool b && b;
                }
            }
            catch { }
            
            // Get local MediaPlayer state (what is actually happening)
            var playerService = Services.RadioPlayerService.Instance;
            bool localMediaPlayerIsPlaying = playerService.IsLocalMediaPlayerPlaying;
            
            // Check if shared state changed since last check
            if (sharedIsPlaying != _lastKnownPlayingState)
            {
                Debug.WriteLine($"[App] Shared state changed: IsPlaying {_lastKnownPlayingState} → {sharedIsPlaying}");
                _lastKnownPlayingState = sharedIsPlaying;
                
                // Sync the local MediaPlayer state to match shared state
                // This is critical: if widget paused, we need to pause the main app's MediaPlayer too
                if (sharedIsPlaying != localMediaPlayerIsPlaying)
                {
                    Debug.WriteLine($"[App] Syncing MediaPlayer: shared={sharedIsPlaying}, localMediaPlayer={localMediaPlayerIsPlaying}");
                    
                    try
                    {
                        if (sharedIsPlaying)
                        {
                            // Shared state says playing, but local MediaPlayer isn't - start it
                            Debug.WriteLine("[App] Starting local MediaPlayer to match shared state");
                            if (!string.IsNullOrEmpty(playerService.StreamUrl))
                            {
                                playerService.Play();
                            }
                        }
                        else
                        {
                            // Shared state says paused, but local MediaPlayer is playing - pause it
                            Debug.WriteLine("[App] Pausing local MediaPlayer to match shared state");
                            playerService.Pause();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Error syncing MediaPlayer state: {ex.Message}");
                    }
                }
                
                // Manually trigger the property changed handler to update UI
                PlayerVmOnPropertyChanged(this, new PropertyChangedEventArgs(nameof(PlayerViewModel.IsPlaying)));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Error checking shared state: {ex.Message}");
        }
    }

    private void StartSharedStatePollingForComServer()
    {
        // Get the dispatcher queue for the current thread
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
        {
            Debug.WriteLine("[App] No DispatcherQueue in COM server mode, cannot start polling");
            return;
        }

        // Create a timer that polls shared state every 2 seconds
        _sharedStatePollingTimer = dispatcherQueue.CreateTimer();
        _sharedStatePollingTimer.Interval = TimeSpan.FromSeconds(2);
        _sharedStatePollingTimer.Tick += (sender, args) =>
        {
            CheckSharedStateForComServer();
        };
        _sharedStatePollingTimer.Start();
        
        // Initialize the last known state
        _lastKnownPlayingState = _playerVm.IsPlaying;
        Debug.WriteLine("[App] Started shared state polling for COM server mode");
    }

    private void CheckSharedStateForComServer()
    {
        try
        {
            // Get shared state (what should be happening)
            bool sharedIsPlaying = false;
            try
            {
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("RadioIsPlaying", out object? storedValue))
                {
                    sharedIsPlaying = storedValue is bool b && b;
                }
            }
            catch { }
            
            // In COM server mode (widget), we should NOT sync the MediaPlayer
            // Only the main app process should actually play audio
            // The widget process only updates shared state, it doesn't play audio itself
            
            // Therefore, we don't sync MediaPlayer in COM server mode
            // This prevents duplicate audio streams
            
            Debug.WriteLine($"[App-COM] Shared state: IsPlaying={sharedIsPlaying} (MediaPlayer sync disabled in COM server mode)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App-COM] Error checking shared state: {ex.Message}");
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
            _widgetRegistrationManager?.Dispose();
            _trayIconWatchdogTimer?.Stop();
            _sharedStatePollingTimer?.Stop();
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
