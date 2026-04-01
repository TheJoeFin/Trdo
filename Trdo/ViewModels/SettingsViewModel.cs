using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Trdo.ViewModels;

public partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerViewModel _playerViewModel;
    private bool _isStartupEnabled;
    private bool _isStartupToggleEnabled = true;
    private string _startupToggleText = "Off";
    private string _watchdogToggleText = "Off";
    private string _autoBufferToggleText = "Off";
    private string _autoPlayOnStartupToggleText = "Off";
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
            else if (args.PropertyName == nameof(PlayerViewModel.SilenceTimeoutSeconds))
            {
                OnPropertyChanged(nameof(SilenceTimeoutSeconds));
                OnPropertyChanged(nameof(SilenceTimeoutDisplay));
            }
        };

        // Initialize toggle text
        WatchdogToggleText = GetToggleText(_playerViewModel.WatchdogEnabled);
        AutoBufferToggleText = GetToggleText(_playerViewModel.AutoBufferIncreaseEnabled);
        AutoPlayOnStartupToggleText = GetToggleText(SettingsService.AutoPlayOnStartup);

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

    /// <summary>
    /// Gets or sets whether the app should automatically start playing the last selected station on startup.
    /// </summary>
    public bool IsAutoPlayOnStartupEnabled
    {
        get => SettingsService.AutoPlayOnStartup;
        set
        {
            if (value == SettingsService.AutoPlayOnStartup) return;
            SettingsService.AutoPlayOnStartup = value;
            OnPropertyChanged();
            AutoPlayOnStartupToggleText = GetToggleText(value);
        }
    }

    public string AutoPlayOnStartupToggleText
    {
        get => _autoPlayOnStartupToggleText;
        set
        {
            if (value == _autoPlayOnStartupToggleText) return;
            _autoPlayOnStartupToggleText = value;
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

    /// <summary>
    /// Gets or sets the silence detection timeout in seconds.
    /// If audio is silent for longer than this, the stream will be restarted.
    /// </summary>
    public double SilenceTimeoutSeconds
    {
        get => _playerViewModel.SilenceTimeoutSeconds;
        set
        {
            if (Math.Abs(value - _playerViewModel.SilenceTimeoutSeconds) < 0.01) return;
            _playerViewModel.SilenceTimeoutSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SilenceTimeoutDisplay));
        }
    }

    /// <summary>
    /// Gets a formatted display string for the current silence timeout value.
    /// </summary>
    public string SilenceTimeoutDisplay => $"{SilenceTimeoutSeconds:0}s";

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

    /// <summary>
    /// Imports radio stations from a playlist file (M3U, M3U8, or PLS).
    /// </summary>
    public async Task<int> ImportStationsAsync(nint windowHandle)
    {
        FileOpenPicker picker = new();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        picker.FileTypeFilter.Add(".pls");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
            return 0;

        string content = await FileIO.ReadTextAsync(file);
        List<RadioStation> imported = PlaylistImportExportService.ImportFromFile(file.Path, content);

        if (imported.Count == 0)
            return 0;

        PlayerViewModel player = PlayerViewModel.Shared;
        int addedCount = 0;
        foreach (RadioStation station in imported)
        {
            bool alreadyExists = false;
            foreach (RadioStation existing in player.Stations)
            {
                if (string.Equals(existing.Name, station.Name, StringComparison.Ordinal) &&
                    string.Equals(existing.StreamUrl, station.StreamUrl, StringComparison.Ordinal) &&
                    string.Equals(existing.Homepage, station.Homepage, StringComparison.Ordinal) &&
                    string.Equals(existing.FaviconUrl, station.FaviconUrl, StringComparison.Ordinal))
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists)
            {
                player.Stations.Add(station);
                addedCount++;
            }
        }

        if (addedCount > 0)
            RadioStationService.Instance.SaveStations(player.Stations);

        return addedCount;
    }

    /// <summary>
    /// Exports all radio stations to a playlist file (M3U, M3U8, or PLS).
    /// </summary>
    public async Task<bool> ExportStationsAsync(nint windowHandle)
    {
        PlayerViewModel player = PlayerViewModel.Shared;
        if (player.Stations.Count == 0)
            return false;

        FileSavePicker picker = new();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        picker.SuggestedFileName = "Trdo Stations";
        picker.FileTypeChoices.Add("M3U Playlist", [".m3u"]);
        picker.FileTypeChoices.Add("PLS Playlist", [".pls"]);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
            return false;

        string extension = Path.GetExtension(file.Name).ToLowerInvariant();
        string content = extension == ".pls"
            ? PlaylistImportExportService.ExportToPls(player.Stations)
            : PlaylistImportExportService.ExportToM3u(player.Stations);

        await FileIO.WriteTextAsync(file, content);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
