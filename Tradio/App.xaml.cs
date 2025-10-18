using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Threading.Tasks;
using Tradio.Controls;
using Tradio.ViewModels;
using WinUIEx;

namespace Tradio;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _trayIcon;
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
        _trayIcon = new(0, "Assets/Radio.ico", "Trdo");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        Flyout flyout = new()
        {
            Content = new ShellPage()
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

        // Choose icon based on play state. When not playing, use Radio-Off.png
        // Fallback to Radio.ico when playing or if the PNG isn't present.
        string iconUri = _playerVm.IsPlaying
            ? "Assets/Radio.ico"
            : "Assets/Radio-Off.ico";

        // TODO: maybe make this a little more robust witha try/catch
        // _window.SetTaskBarIcon(Icon.FromFile(iconUri));
        _trayIcon.SetIcon(iconUri);
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
}
