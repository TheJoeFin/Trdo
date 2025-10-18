using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tradio.Models;

namespace Tradio.ViewModels;

public class AddStationViewModel : INotifyPropertyChanged
{
    private string _stationName = string.Empty;
    private string _streamUrl = string.Empty;
    private bool _hasValidationError;
    private string _validationMessage = string.Empty;
    private string _pageTitle = "Add Radio Station";
    private PlayerViewModel? _playerViewModel;
    private RadioStation? _editingStation;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RadioStation>? StationAdded;

    public void SetPlayerViewModel(PlayerViewModel playerViewModel)
    {
        _playerViewModel = playerViewModel;
    }

    public void LoadStationForEdit(RadioStation station)
    {
        _editingStation = station;
        StationName = station.Name;
        StreamUrl = station.StreamUrl;
        PageTitle = "Edit Radio Station";
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set
        {
            if (value == _pageTitle) return;
            _pageTitle = value;
            OnPropertyChanged();
        }
    }

    public string StationName
    {
        get => _stationName;
        set
        {
            if (value == _stationName) return;
            _stationName = value;
            OnPropertyChanged();
            ValidateInput();
        }
    }

    public string StreamUrl
    {
        get => _streamUrl;
        set
        {
            if (value == _streamUrl) return;
            _streamUrl = value;
            OnPropertyChanged();
            ValidateInput();
        }
    }

    public bool HasValidationError
    {
        get => _hasValidationError;
        private set
        {
            if (value == _hasValidationError) return;
            _hasValidationError = value;
            OnPropertyChanged();
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (value == _validationMessage) return;
            _validationMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(StationName) && 
                           !string.IsNullOrWhiteSpace(StreamUrl) && 
                           !HasValidationError;

    private void ValidateInput()
    {
        HasValidationError = false;
        ValidationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(StationName) && string.IsNullOrWhiteSpace(StreamUrl))
        {
            // Don't show error if both are empty (initial state)
            OnPropertyChanged(nameof(CanSave));
            return;
        }

        if (string.IsNullOrWhiteSpace(StationName))
        {
            HasValidationError = true;
            ValidationMessage = "Station name is required.";
        }
        else if (string.IsNullOrWhiteSpace(StreamUrl))
        {
            HasValidationError = true;
            ValidationMessage = "Stream URL is required.";
        }
        else if (!IsValidUrl(StreamUrl))
        {
            HasValidationError = true;
            ValidationMessage = "Please enter a valid HTTP or HTTPS URL.";
        }

        OnPropertyChanged(nameof(CanSave));
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public bool Save()
    {
        ValidateInput();
        
        if (!CanSave)
        {
            return false;
        }

        if (_editingStation != null)
        {
            // Edit mode - update existing station
            _editingStation.Name = StationName.Trim();
            _editingStation.StreamUrl = StreamUrl.Trim();
            
            // Save the updated stations list
            _playerViewModel?.SaveStations();
            
            // If this was the selected station, trigger update to reload the stream
            if (_playerViewModel?.SelectedStation == _editingStation)
            {
                // Re-apply the stream URL if it changed
                _playerViewModel.ApplyStreamUrl();
            }
        }
        else
        {
            // Add mode - create new station
            var newStation = new RadioStation
            {
                Name = StationName.Trim(),
                StreamUrl = StreamUrl.Trim()
            };

            // Add to PlayerViewModel if available
            if (_playerViewModel != null)
            {
                _playerViewModel.AddStation(newStation);
            }

            // Raise event for listeners
            StationAdded?.Invoke(this, newStation);
        }
        
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
