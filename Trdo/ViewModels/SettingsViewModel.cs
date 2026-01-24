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
    private string _autoBufferToggleText = "Off";
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
                WatchdogToggleText = GetToggleText(_playerViewModel.WatchdogEnabled);
            }
            else if (args.PropertyName == nameof(PlayerViewModel.AutoBufferIncreaseEnabled))
            {
                OnPropertyChanged(nameof(IsAutoBufferIncreaseEnabled));
                AutoBufferToggleText = GetToggleText(_playerViewModel.AutoBufferIncreaseEnabled);
            }
            else if (args.PropertyName == nameof(PlayerViewModel.BufferLevel))
            {
                OnPropertyChanged(nameof(BufferLevel));
                OnPropertyChanged(nameof(BufferLevelDescription));
            }
        };

        // Initialize toggle text
        WatchdogToggleText = GetToggleText(_playerViewModel.WatchdogEnabled);
        AutoBufferToggleText = GetToggleText(_playerViewModel.AutoBufferIncreaseEnabled);

        // Initialize startup task
        _ = InitializeStartupTaskAsync();
    }

    private static string GetToggleText(bool enabled) => enabled ? "On" : "Off";

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set
        {
            if (value == _isStartupEnabled) return;
            _isStartupEnabled = value;
            OnPropertyChanged();
            StartupToggleText = GetToggleText(value);

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
            WatchdogToggleText = GetToggleText(value);
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

    /// <summary>
    /// Gets or sets whether auto-buffer increase is enabled.
    /// When enabled, the buffer level automatically increases when stutter is detected.
    /// </summary>
    public bool IsAutoBufferIncreaseEnabled
    {
        get => _playerViewModel.AutoBufferIncreaseEnabled;
        set
        {
            if (value == _playerViewModel.AutoBufferIncreaseEnabled) return;
            _playerViewModel.AutoBufferIncreaseEnabled = value;
            OnPropertyChanged();
            AutoBufferToggleText = GetToggleText(value);
        }
    }

    public string AutoBufferToggleText
    {
        get => _autoBufferToggleText;
        set
        {
            if (value == _autoBufferToggleText) return;
            _autoBufferToggleText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the current buffer level (0-3).
    /// 0 = Default, 1 = Medium, 2 = Large, 3 = Extra Large
    /// </summary>
    public double BufferLevel
    {
        get => _playerViewModel.BufferLevel;
        set
        {
            if (Math.Abs(value - _playerViewModel.BufferLevel) < 0.0001) return;
            _playerViewModel.BufferLevel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BufferLevelDescription));
        }
    }

    /// <summary>
    /// Gets a human-readable description of the current buffer level.
    /// </summary>
    public string BufferLevelDescription => _playerViewModel.BufferLevelDescription;

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
                StartupToggleText = GetToggleText(true);
                break;
            case StartupTaskState.Disabled:
                IsStartupToggleEnabled = true;
                _isStartupEnabled = false;
                OnPropertyChanged(nameof(IsStartupEnabled));
                StartupToggleText = GetToggleText(false);
                break;
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
            default:
                IsStartupToggleEnabled = false;
                _isStartupEnabled = false;
                OnPropertyChanged(nameof(IsStartupEnabled));
                StartupToggleText = GetToggleText(false);
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
