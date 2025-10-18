using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tradio.ViewModels;

public class AddStationViewModel : INotifyPropertyChanged
{
    private string _stationName = string.Empty;
    private string _streamUrl = string.Empty;
    private bool _hasValidationError;
    private string _validationMessage = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

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

        // TODO: Implement actual save logic to persist the station
        // For now, just return true to indicate success
        // This should add to PlayerViewModel.Stations or save to storage
        
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
