using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Trdo.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerViewModel _playerViewModel;
    private bool _isStartupEnabled;
    private bool _isStartupToggleEnabled = true;
    private string _startupToggleText = "Off";
    private string _watchdogToggleText = "Off";
    private StartupTask? _startupTask;
    private bool _initDone;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel()
    {
        _playerViewModel = new PlayerViewModel();

        // Subscribe to PlayerViewModel property changes
        _playerViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PlayerViewModel.WatchdogEnabled))
            {
                OnPropertyChanged(nameof(IsWatchdogEnabled));
                WatchdogToggleText = _playerViewModel.WatchdogEnabled ? "On" : "Off";
            }
        };

        // Initialize watchdog toggle text
        WatchdogToggleText = _playerViewModel.WatchdogEnabled ? "On" : "Off";

        // Initialize startup task
        _ = InitializeStartupTaskAsync();
    }

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set
        {
            if (value == _isStartupEnabled) return;
            _isStartupEnabled = value;
            OnPropertyChanged();
            StartupToggleText = value ? "On" : "Off";

            // Apply the change
            _ = ApplyStartupStateAsync(value);
        }
    }

    public bool IsStartupToggleEnabled
    {
        get => _isStartupToggleEnabled;
        set
        {
            if (value == _isStartupToggleEnabled) return;
            _isStartupToggleEnabled = value;
            OnPropertyChanged();
        }
    }

    public string StartupToggleText
    {
        get => _startupToggleText;
        set
        {
            if (value == _startupToggleText) return;
            _startupToggleText = value;
            OnPropertyChanged();
        }
    }

    public bool IsWatchdogEnabled
    {
        get => _playerViewModel.WatchdogEnabled;
        set
        {
            if (value == _playerViewModel.WatchdogEnabled) return;
            _playerViewModel.WatchdogEnabled = value;
            OnPropertyChanged();
            WatchdogToggleText = value ? "On" : "Off";
        }
    }

    public string WatchdogToggleText
    {
        get => _watchdogToggleText;
        set
        {
            if (value == _watchdogToggleText) return;
            _watchdogToggleText = value;
            OnPropertyChanged();
        }
    }

    private async Task InitializeStartupTaskAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync("TrdoStartup").AsTask();
            _initDone = true;
            UpdateStartupStateFromTask();
        }
        catch
        {
            // Could not get StartupTask (likely unpackaged). Disable toggle.
            IsStartupToggleEnabled = false;
            IsStartupEnabled = false;
        }
    }

    private void UpdateStartupStateFromTask()
    {
        if (_startupTask is null) return;

        switch (_startupTask.State)
        {
            case StartupTaskState.Enabled:
                IsStartupToggleEnabled = true;
                _isStartupEnabled = true;
                OnPropertyChanged(nameof(IsStartupEnabled));
                StartupToggleText = "On";
                break;
            case StartupTaskState.Disabled:
                IsStartupToggleEnabled = true;
                _isStartupEnabled = false;
                OnPropertyChanged(nameof(IsStartupEnabled));
                StartupToggleText = "Off";
                break;
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
            default:
                IsStartupToggleEnabled = false;
                _isStartupEnabled = false;
                OnPropertyChanged(nameof(IsStartupEnabled));
                StartupToggleText = "Off";
                break;
        }
    }

    private async Task ApplyStartupStateAsync(bool enable)
    {
        if (!_initDone || _startupTask is null) return;

        try
        {
            if (enable)
            {
                if (_startupTask.State == StartupTaskState.Disabled)
                {
                    await _startupTask.RequestEnableAsync().AsTask();
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
        UpdateStartupStateFromTask();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
