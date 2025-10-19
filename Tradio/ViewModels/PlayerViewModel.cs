using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

public sealed partial class PlayerViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private readonly RadioStationService _stationService = RadioStationService.Instance;
    private string _watchdogStatus = string.Empty;
    private RadioStation? _selectedStation;
    private string? _lastError;

    private static readonly Lazy<PlayerViewModel> _instance = new(() => new PlayerViewModel());
    public static PlayerViewModel Shared => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? PlaybackError;

    public PlayerViewModel()
    {
        Debug.WriteLine("=== PlayerViewModel Constructor START ===");
        
        _player.PlaybackStateChanged += (_, _) =>
        {
            Debug.WriteLine($"[PlayerViewModel] PlaybackStateChanged event fired. IsPlaying={IsPlaying}");
            OnPropertyChanged(nameof(IsPlaying));
        };
        
        _player.VolumeChanged += (_, _) =>
        {
            Debug.WriteLine($"[PlayerViewModel] VolumeChanged event fired. Volume={Volume}");
            OnPropertyChanged(nameof(Volume));
        };

        // Subscribe to watchdog status changes
        _player.Watchdog.StreamStatusChanged += (_, args) =>
        {
            WatchdogStatus = $"{args.Status}: {args.Message}";
            Debug.WriteLine($"[PlayerViewModel] Watchdog status: {WatchdogStatus}");
        };

        // Load stations from settings
        Debug.WriteLine("[PlayerViewModel] Loading stations from settings...");
        List<RadioStation> loadedStations = _stationService.LoadStations();
        Debug.WriteLine($"[PlayerViewModel] Loaded {loadedStations.Count} stations");
        Stations = new ObservableCollection<RadioStation>(loadedStations);

        // Load the previously selected station
        int selectedIndex = _stationService.LoadSelectedStationIndex();
        Debug.WriteLine($"[PlayerViewModel] Previously selected station index: {selectedIndex}");
        
        if (selectedIndex >= 0 && selectedIndex < Stations.Count)
        {
            _selectedStation = Stations[selectedIndex];
            Debug.WriteLine($"[PlayerViewModel] Restored selected station: {_selectedStation.Name} ({_selectedStation.StreamUrl})");
        }
        else if (Stations.Count > 0)
        {
            _selectedStation = Stations[0];
            Debug.WriteLine($"[PlayerViewModel] No valid saved index, selecting first station: {_selectedStation.Name} ({_selectedStation.StreamUrl})");
        }
        else
        {
            Debug.WriteLine("[PlayerViewModel] No stations available");
        }

        // Initialize with selected station's URL if available
        if (_selectedStation != null)
        {
            Debug.WriteLine($"[PlayerViewModel] Initializing stream with URL: {_selectedStation.StreamUrl}");
            InitializeStream(_selectedStation.StreamUrl);
        }
        else
        {
            Debug.WriteLine("[PlayerViewModel] No selected station to initialize");
        }
        
        Debug.WriteLine("=== PlayerViewModel Constructor END ===");
    }

    public ObservableCollection<RadioStation> Stations { get; }

    public RadioStation? SelectedStation
    {
        get => _selectedStation;
        set
        {
            Debug.WriteLine($"=== SelectedStation SETTER START ===");
            Debug.WriteLine($"[PlayerViewModel] Current station: {(_selectedStation?.Name ?? "null")}");
            Debug.WriteLine($"[PlayerViewModel] New station: {(value?.Name ?? "null")}");
            
            if (value == _selectedStation)
            {
                Debug.WriteLine("[PlayerViewModel] Same station selected, no change needed");
                Debug.WriteLine($"=== SelectedStation SETTER END (no change) ===");
                return;
            }
            
            bool wasPlaying = IsPlaying;
            Debug.WriteLine($"[PlayerViewModel] Was playing before station change: {wasPlaying}");
            
            _selectedStation = value;
            OnPropertyChanged();

            if (_selectedStation != null)
            {
                Debug.WriteLine($"[PlayerViewModel] New selected station: {_selectedStation.Name}");
                Debug.WriteLine($"[PlayerViewModel] Stream URL: {_selectedStation.StreamUrl}");
                
                // Save the selected station index
                int index = Stations.IndexOf(_selectedStation);
                Debug.WriteLine($"[PlayerViewModel] Station index in collection: {index}");
                if (index >= 0)
                {
                    _stationService.SaveSelectedStationIndex(index);
                    Debug.WriteLine($"[PlayerViewModel] Saved station index {index} to settings");
                }

                // Validate the URL
                if (!IsValidUrl(_selectedStation.StreamUrl))
                {
                    _lastError = $"Invalid stream URL for {_selectedStation.Name}";
                    Debug.WriteLine($"[PlayerViewModel] ERROR: {_lastError}");
                    PlaybackError?.Invoke(this, _lastError);
                    if (wasPlaying)
                    {
                        Debug.WriteLine("[PlayerViewModel] Pausing player due to invalid URL");
                        _player.Pause();
                    }
                    Debug.WriteLine($"=== SelectedStation SETTER END (invalid URL) ===");
                    return;
                }

                try
                {
                    // Stop current playback
                    if (wasPlaying)
                    {
                        Debug.WriteLine("[PlayerViewModel] Pausing current playback before switching...");
                        _player.Pause();
                    }

                    // Set the new stream URL
                    Debug.WriteLine($"[PlayerViewModel] Setting stream URL in player: {_selectedStation.StreamUrl}");
                    _player.SetStreamUrl(_selectedStation.StreamUrl);
                    Debug.WriteLine("[PlayerViewModel] Stream URL set successfully");
                    
                    // Resume playback if we were playing before
                    if (wasPlaying)
                    {
                        Debug.WriteLine("[PlayerViewModel] Resuming playback with new station...");
                        _player.Play();
                        Debug.WriteLine("[PlayerViewModel] Play command sent to player");
                    }
                    
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    _lastError = $"Failed to switch to {_selectedStation.Name}: {ex.Message}";
                    Debug.WriteLine($"[PlayerViewModel] EXCEPTION: {_lastError}");
                    Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                    PlaybackError?.Invoke(this, _lastError);
                }
            }
            else
            {
                Debug.WriteLine("[PlayerViewModel] Selected station is null");
            }
            
            Debug.WriteLine($"=== SelectedStation SETTER END ===");
        }
    }

    public bool IsPlaying
    {
        get
        {
            bool isPlaying = _player.IsPlaying;
            Debug.WriteLine($"[PlayerViewModel] IsPlaying getter called, value: {isPlaying}");
            return isPlaying;
        }
    }

    public string StreamUrl
    {
        get
        {
            string url = _player.StreamUrl ?? string.Empty;
            Debug.WriteLine($"[PlayerViewModel] StreamUrl getter called, value: {url}");
            return url;
        }
    }

    public bool WatchdogEnabled
    {
        get => _player.WatchdogEnabled;
        set
        {
            if (value == _player.WatchdogEnabled) return;
            Debug.WriteLine($"[PlayerViewModel] Setting WatchdogEnabled to {value}");
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

    public string? LastError => _lastError;

    public double Volume
    {
        get => _player.Volume;
        set
        {
            Debug.WriteLine($"[PlayerViewModel] Setting Volume to {value}");
            _player.Volume = value;
            OnPropertyChanged();
        }
    }

    public void Toggle()
    {
        Debug.WriteLine("=== Toggle START ===");
        Debug.WriteLine($"[PlayerViewModel] Current IsPlaying: {IsPlaying}");
        Debug.WriteLine($"[PlayerViewModel] Selected station: {(_selectedStation?.Name ?? "null")}");
        Debug.WriteLine($"[PlayerViewModel] Current stream URL in player: {_player.StreamUrl ?? "null"}");
        
        try
        {
            _player.TogglePlayPause();
            Debug.WriteLine($"[PlayerViewModel] TogglePlayPause called successfully. New IsPlaying: {IsPlaying}");
            _lastError = null;
        }
        catch (Exception ex)
        {
            string stationName = _selectedStation?.Name ?? "Unknown";
            _lastError = $"Failed to play {stationName}: {ex.Message}";
            Debug.WriteLine($"[PlayerViewModel] EXCEPTION in Toggle: {_lastError}");
            Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
            PlaybackError?.Invoke(this, _lastError);
        }
        
        Debug.WriteLine("=== Toggle END ===");
    }

    /// <summary>
    /// Add a new station and save to settings
    /// </summary>
    public void AddStation(RadioStation station)
    {
        if (station == null) return;

        Debug.WriteLine($"[PlayerViewModel] Adding station: {station.Name} ({station.StreamUrl})");
        Stations.Add(station);
        _stationService.SaveStations(Stations);
        
        // If this is the first station, select it automatically
        if (Stations.Count == 1)
        {
            Debug.WriteLine("[PlayerViewModel] First station added, selecting automatically");
            SelectedStation = station;
        }
    }

    /// <summary>
    /// Remove a station and save to settings
    /// </summary>
    public void RemoveStation(RadioStation station)
    {
        if (station == null) return;

        Debug.WriteLine($"[PlayerViewModel] Removing station: {station.Name} ({station.StreamUrl})");

        // If removing the selected station, select another one first
        if (station == _selectedStation)
        {
            Debug.WriteLine("[PlayerViewModel] Removing currently selected station");
            if (Stations.Count > 1)
            {
                int currentIndex = Stations.IndexOf(station);
                int newIndex = currentIndex > 0 ? currentIndex - 1 : 1;
                Debug.WriteLine($"[PlayerViewModel] Selecting station at index {newIndex}");
                SelectedStation = Stations[newIndex];
            }
            else
            {
                // Last station - stop playback and clear selection
                Debug.WriteLine("[PlayerViewModel] Removing last station, stopping playback");
                if (IsPlaying)
                {
                    _player.Pause();
                }
                _selectedStation = null;
                OnPropertyChanged(nameof(SelectedStation));
            }
        }

        Stations.Remove(station);
        _stationService.SaveStations(Stations);
        Debug.WriteLine($"[PlayerViewModel] Station removed, {Stations.Count} stations remaining");
    }

    /// <summary>
    /// Save the current stations list to settings (used when editing stations)
    /// </summary>
    public void SaveStations()
    {
        Debug.WriteLine("[PlayerViewModel] SaveStations called");
        _stationService.SaveStations(Stations);
        
        // If the current station was edited, reinitialize the stream
        if (_selectedStation != null && IsValidUrl(_selectedStation.StreamUrl))
        {
            Debug.WriteLine($"[PlayerViewModel] Reinitializing stream after save: {_selectedStation.StreamUrl}");
            try
            {
                bool wasPlaying = IsPlaying;
                if (wasPlaying)
                {
                    Debug.WriteLine("[PlayerViewModel] Pausing before reinitialize");
                    _player.Pause();
                }
                
                _player.SetStreamUrl(_selectedStation.StreamUrl);
                Debug.WriteLine("[PlayerViewModel] Stream URL updated");
                
                if (wasPlaying)
                {
                    Debug.WriteLine("[PlayerViewModel] Resuming playback");
                    _player.Play();
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to update stream: {ex.Message}";
                Debug.WriteLine($"[PlayerViewModel] EXCEPTION in SaveStations: {_lastError}");
                Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                PlaybackError?.Invoke(this, _lastError);
            }
        }
    }

    private void InitializeStream(string streamUrl)
    {
        Debug.WriteLine($"[PlayerViewModel] InitializeStream called with URL: {streamUrl}");
        if (IsValidUrl(streamUrl))
        {
            try
            {
                _player.Initialize(streamUrl);
                Debug.WriteLine($"[PlayerViewModel] Player initialized with URL: {streamUrl}");
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to initialize stream: {ex.Message}";
                Debug.WriteLine($"[PlayerViewModel] EXCEPTION in InitializeStream: {_lastError}");
                Debug.WriteLine($"[PlayerViewModel] Exception details: {ex}");
                PlaybackError?.Invoke(this, _lastError);
            }
        }
        else
        {
            Debug.WriteLine($"[PlayerViewModel] Invalid URL, skipping initialization: {streamUrl}");
        }
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        Debug.WriteLine($"[PlayerViewModel] PropertyChanged: {name}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}