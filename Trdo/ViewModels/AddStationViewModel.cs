using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Trdo.Models;

namespace Trdo.ViewModels;

public class AddStationViewModel : INotifyPropertyChanged
{
    private string _stationName = string.Empty;
    private string _streamUrl = string.Empty;
    private string? _homepage;
    private string? _faviconUrl;
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
        Homepage = station.Homepage;
        FaviconUrl = station.FaviconUrl;
        PageTitle = "Edit Radio Station";
    }

    public void LoadFromSearchResult(RadioBrowserStation searchStation)
    {
        StationName = searchStation.Name;
        StreamUrl = searchStation.GetStreamUrl();
        Homepage = !string.IsNullOrWhiteSpace(searchStation.Homepage) ? searchStation.Homepage : null;
        FaviconUrl = !string.IsNullOrWhiteSpace(searchStation.Favicon) ? searchStation.Favicon : null;
        PageTitle = "Add Radio Station";
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

    public string? Homepage
    {
        get => _homepage;
        set
        {
            if (value == _homepage) return;
            _homepage = value;
            OnPropertyChanged();
        }
    }

    public string? FaviconUrl
    {
        get => _faviconUrl;
        set
        {
            if (value == _faviconUrl) return;
            _faviconUrl = value;
            OnPropertyChanged();
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
            _editingStation.Homepage = !string.IsNullOrWhiteSpace(Homepage) ? Homepage.Trim() : null;
            _editingStation.FaviconUrl = !string.IsNullOrWhiteSpace(FaviconUrl) ? FaviconUrl.Trim() : null;

            // Save the updated stations list - this will automatically reinitialize if it's the selected station
            _playerViewModel?.SaveStations();
        }
        else
        {
            // Add mode - create new station
            RadioStation newStation = new()
            {
                Name = StationName.Trim(),
                StreamUrl = StreamUrl.Trim(),
                Homepage = !string.IsNullOrWhiteSpace(Homepage) ? Homepage.Trim() : null,
                FaviconUrl = !string.IsNullOrWhiteSpace(FaviconUrl) ? FaviconUrl.Trim() : null
            };

            // Add to PlayerViewModel if available
            _playerViewModel?.AddStation(newStation);

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
