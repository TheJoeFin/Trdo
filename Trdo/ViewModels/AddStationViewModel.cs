using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

public class AddStationViewModel : INotifyPropertyChanged
{
    private string _stationName = string.Empty;
    private string _streamUrl = string.Empty;
    private bool _hasValidationError;
    private string _validationMessage = string.Empty;
    private string _pageTitle = "Add Radio Station";
    private PlayerViewModel? _playerViewModel;
    private RadioStation? _editingStation;
    private string _searchTerm = string.Empty;
    private bool _isSearching;
    private bool _isManualMode = true;
    private RadioBrowserStation? _selectedSearchResult;
    private readonly RadioBrowserService _radioBrowserService = new();
    private CancellationTokenSource? _searchCancellationTokenSource;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RadioStation>? StationAdded;

    public ObservableCollection<RadioBrowserStation> SearchResults { get; } = new();

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
        // Force manual mode when editing
        IsManualMode = true;
    }

    public bool IsManualMode
    {
        get => _isManualMode;
        set
        {
            if (value == _isManualMode) return;
            _isManualMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearchMode));
            
            // Clear search results when switching modes
            if (!_isManualMode)
            {
                SearchResults.Clear();
                SearchTerm = string.Empty;
            }
        }
    }

    public bool IsSearchMode => !_isManualMode;

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (value == _searchTerm) return;
            _searchTerm = value;
            OnPropertyChanged();
            
            // Trigger search after a short delay
            _ = PerformSearchAsync();
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (value == _isSearching) return;
            _isSearching = value;
            OnPropertyChanged();
        }
    }

    public RadioBrowserStation? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (value == _selectedSearchResult) return;
            _selectedSearchResult = value;
            OnPropertyChanged();

            // Auto-populate fields when a station is selected
            if (value != null)
            {
                StationName = value.Name;
                StreamUrl = value.GetStreamUrl();
            }
        }
    }

    private async Task PerformSearchAsync()
    {
        // Cancel any ongoing search
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource = new CancellationTokenSource();

        // Wait a bit for the user to finish typing
        try
        {
            await Task.Delay(500, _searchCancellationTokenSource.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            SearchResults.Clear();
            return;
        }

        IsSearching = true;

        try
        {
            var results = await _radioBrowserService.SearchByNameAsync(
                SearchTerm,
                limit: 50,
                cancellationToken: _searchCancellationTokenSource.Token);

            SearchResults.Clear();
            foreach (var station in results)
            {
                SearchResults.Add(station);
            }

            Debug.WriteLine($"[AddStationViewModel] Search completed. Found {SearchResults.Count} stations");
        }
        catch (TaskCanceledException)
        {
            // Search was cancelled, ignore
            Debug.WriteLine("[AddStationViewModel] Search cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AddStationViewModel] Search error: {ex.Message}");
            // Could add error handling here
        }
        finally
        {
            IsSearching = false;
        }
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

            // Save the updated stations list - this will automatically reinitialize if it's the selected station
            _playerViewModel?.SaveStations();
        }
        else
        {
            // Add mode - create new station
            RadioStation newStation = new()
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
