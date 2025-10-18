using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tradio.Models;
using Tradio.Services;

namespace Tradio.ViewModels;

public sealed partial class PlayerViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private const string DefaultStreamUrl = "https://wyms.streamguys1.com/live?platform=NPR&uuid=xhjlsf05e";
    private string _streamUrl;
    private string _watchdogStatus = string.Empty;
    private RadioStation? _selectedStation;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel()
    {
        _streamUrl = DefaultStreamUrl;
        _player.Initialize(_streamUrl);
        _player.PlaybackStateChanged += (_, _) => OnPropertyChanged(nameof(IsPlaying));
        _player.VolumeChanged += (_, _) => OnPropertyChanged(nameof(Volume));
        
        // Subscribe to watchdog status changes
        _player.Watchdog.StreamStatusChanged += (_, args) =>
        {
            WatchdogStatus = $"{args.Status}: {args.Message}";
        };

        // Initialize stations
        Stations = new ObservableCollection<RadioStation>
        {
            new RadioStation { Name = "WUWM - Milwaukee's NPR", StreamUrl = "https://wyms.streamguys1.com/live?platform=NPR&uuid=xhjlsf05e" },
            new RadioStation { Name = "88nine Radio Milwaukee", StreamUrl = "https://wmse.streamguys1.com/witr-hi-mp3" },
            new RadioStation { Name = "WXRW - Riverwest Radio", StreamUrl = "https://wxrw.radioca.st/stream" }
        };

        // Set default selected station
        _selectedStation = Stations[1]; // 88nine Radio Milwaukee
    }

    public ObservableCollection<RadioStation> Stations { get; }

    public RadioStation? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (value == _selectedStation) return;
            _selectedStation = value;
            OnPropertyChanged();

            // Update stream URL when station changes
            if (_selectedStation != null)
            {
                StreamUrl = _selectedStation.StreamUrl;
                ApplyStreamUrl();
            }
        }
    }

    public bool IsPlaying => _player.IsPlaying;

    public string StreamUrl
    {
        get => _streamUrl;
        set
        {
            if (value == _streamUrl) return;
            _streamUrl = value;
            OnPropertyChanged();
        }
    }

    public bool WatchdogEnabled
    {
        get => _player.WatchdogEnabled;
        set
        {
            if (value == _player.WatchdogEnabled) return;
            _player.WatchdogEnabled = value;
            OnPropertyChanged();
        }
    }

    public string WatchdogStatus
    {
        get => _watchdogStatus;
        private set
        {
            if (value == _watchdogStatus) return;
            _watchdogStatus = value;
            OnPropertyChanged();
        }
    }

    public bool ApplyStreamUrl()
    {
        if (!IsValidUrl(_streamUrl))
            return false;
        _player.SetStreamUrl(_streamUrl);
        return true;
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public double Volume
    {
        get => _player.Volume;
        set
        {
            _player.Volume = value;
            OnPropertyChanged();
        }
    }

    public void Toggle() => _player.TogglePlayPause();

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}