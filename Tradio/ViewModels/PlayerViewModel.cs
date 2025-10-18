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
    private readonly RadioStationService _stationService = RadioStationService.Instance;
    private const string DefaultStreamUrl = "https://wyms.streamguys1.com/live?platform=NPR&uuid=xhjlsf05e";
    private string _streamUrl;
    private string _watchdogStatus = string.Empty;
    private RadioStation? _selectedStation;

    private static readonly Lazy<PlayerViewModel> _instance = new(() => new PlayerViewModel());
    public static PlayerViewModel Shared => _instance.Value;

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

        // Load stations from settings
        var loadedStations = _stationService.LoadStations();
        Stations = new ObservableCollection<RadioStation>(loadedStations);

        // Load the previously selected station
        int selectedIndex = _stationService.LoadSelectedStationIndex();
        if (selectedIndex >= 0 && selectedIndex < Stations.Count)
        {
            _selectedStation = Stations[selectedIndex];
        }
        else if (Stations.Count > 0)
        {
            _selectedStation = Stations[0];
        }

        // Initialize with selected station's URL if available
        if (_selectedStation != null)
        {
            _streamUrl = _selectedStation.StreamUrl;
            _player.Initialize(_streamUrl);
        }
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

            // Save the selected station index
            if (_selectedStation != null)
            {
                int index = Stations.IndexOf(_selectedStation);
                if (index >= 0)
                {
                    _stationService.SaveSelectedStationIndex(index);
                }

                // Update stream URL when station changes
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

    /// <summary>
    /// Add a new station and save to settings
    /// </summary>
    public void AddStation(RadioStation station)
    {
        if (station == null) return;

        Stations.Add(station);
        _stationService.SaveStations(Stations);
    }

    /// <summary>
    /// Remove a station and save to settings
    /// </summary>
    public void RemoveStation(RadioStation station)
    {
        if (station == null) return;

        // If removing the selected station, select another one first
        if (station == _selectedStation && Stations.Count > 1)
        {
            int currentIndex = Stations.IndexOf(station);
            int newIndex = currentIndex > 0 ? currentIndex - 1 : 1;
            SelectedStation = Stations[newIndex];
        }

        Stations.Remove(station);
        _stationService.SaveStations(Stations);
    }

    /// <summary>
    /// Save the current stations list to settings (used when editing stations)
    /// </summary>
    public void SaveStations()
    {
        _stationService.SaveStations(Stations);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}