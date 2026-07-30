using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

/// <summary>
/// A named codec choice for the codec filter dropdown.
/// An empty <see cref="Value"/> means "any".
/// </summary>
public record CodecOption(string Display, string Value);

/// <summary>
/// A minimum-bitrate choice for the bitrate filter dropdown.
/// A <see cref="Value"/> of 0 means "any".
/// </summary>
public record BitrateOption(string Display, int Value);

/// <summary>
/// A sort choice mapping to the Radio Browser API's order/reverse parameters.
/// </summary>
public record SortOption(string Display, string Order, bool Reverse);

public class SearchStationViewModel : INotifyPropertyChanged
{
    private readonly RadioBrowserService _radioBrowserService = new();
    private CancellationTokenSource? _searchCancellationTokenSource;
    private bool _filterOptionsLoaded;

    private string _searchTerm = string.Empty;
    private bool _isSearching;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private bool _isLoadingFilterOptions;

    private RadioBrowserCountry? _selectedCountry;
    private RadioBrowserLanguage? _selectedLanguage;
    private RadioBrowserTag? _selectedGenre;
    private CodecOption? _selectedCodec;
    private BitrateOption? _selectedBitrate;
    private bool _hideBroken;
    private SortOption _selectedSort;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SearchStationViewModel()
    {
        _selectedSort = SortOptions[0];
    }

    // Results and filter option sources ---------------------------------

    public ObservableCollection<RadioBrowserStation> SearchResults { get; } = [];
    public ObservableCollection<RadioBrowserCountry> Countries { get; } = [];
    public ObservableCollection<RadioBrowserLanguage> Languages { get; } = [];
    public ObservableCollection<RadioBrowserTag> Genres { get; } = [];

    public IReadOnlyList<CodecOption> Codecs { get; } =
    [
        new("Any codec", string.Empty),
        new("MP3", "MP3"),
        new("AAC", "AAC"),
        new("AAC+", "AAC+"),
        new("OGG", "OGG"),
        new("FLAC", "FLAC"),
    ];

    public IReadOnlyList<BitrateOption> Bitrates { get; } =
    [
        new("Any bitrate", 0),
        new("64 kbps+", 64),
        new("128 kbps+", 128),
        new("192 kbps+", 192),
        new("256 kbps+", 256),
        new("320 kbps+", 320),
    ];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new("Most voted", "votes", true),
        new("Most popular", "clickcount", true),
        new("Name (A–Z)", "name", false),
        new("Random", "random", false),
    ];

    // Search box and status ---------------------------------------------

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (value == _searchTerm) return;
            _searchTerm = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInitialState));
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
            OnPropertyChanged(nameof(ShowInitialState));
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (value == _hasError) return;
            _hasError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInitialState));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (value == _errorMessage) return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingFilterOptions
    {
        get => _isLoadingFilterOptions;
        private set
        {
            if (value == _isLoadingFilterOptions) return;
            _isLoadingFilterOptions = value;
            OnPropertyChanged();
        }
    }

    // Filter selections (each change re-runs the search) ----------------

    public RadioBrowserCountry? SelectedCountry
    {
        get => _selectedCountry;
        set => SetFilter(ref _selectedCountry, value);
    }

    public RadioBrowserLanguage? SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetFilter(ref _selectedLanguage, value);
    }

    public RadioBrowserTag? SelectedGenre
    {
        get => _selectedGenre;
        set => SetFilter(ref _selectedGenre, value);
    }

    public CodecOption? SelectedCodec
    {
        get => _selectedCodec;
        set => SetFilter(ref _selectedCodec, value);
    }

    public BitrateOption? SelectedBitrate
    {
        get => _selectedBitrate;
        set => SetFilter(ref _selectedBitrate, value);
    }

    public bool HideBroken
    {
        get => _hideBroken;
        set => SetFilter(ref _hideBroken, value);
    }

    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (Equals(value, _selectedSort)) return;
            _selectedSort = value;
            OnPropertyChanged();
            _ = PerformSearchAsync();
        }
    }

    // Computed state -----------------------------------------------------

    public bool HasActiveFilters =>
        SelectedCountry is not null ||
        SelectedLanguage is not null ||
        SelectedGenre is not null ||
        !string.IsNullOrEmpty(SelectedCodec?.Value) ||
        (SelectedBitrate?.Value ?? 0) > 0 ||
        HideBroken;

    public bool ShowInitialState => string.IsNullOrWhiteSpace(SearchTerm) &&
                !HasActiveFilters &&
                SearchResults.Count == 0 &&
                !IsSearching &&
                !HasError;

    /// <summary>
    /// Loads the country/language/genre dropdown options the first time the filter panel opens.
    /// </summary>
    public async Task LoadFilterOptionsAsync()
    {
        if (_filterOptionsLoaded || IsLoadingFilterOptions)
        {
            return;
        }

        IsLoadingFilterOptions = true;
        try
        {
            List<RadioBrowserCountry> countries = await _radioBrowserService.GetCountriesAsync();
            List<RadioBrowserLanguage> languages = await _radioBrowserService.GetLanguagesAsync();
            List<RadioBrowserTag> tags = await _radioBrowserService.GetTagsAsync();

            Countries.Clear();
            foreach (RadioBrowserCountry country in countries)
            {
                Countries.Add(country);
            }

            Languages.Clear();
            foreach (RadioBrowserLanguage language in languages)
            {
                Languages.Add(language);
            }

            Genres.Clear();
            foreach (RadioBrowserTag tag in tags)
            {
                Genres.Add(tag);
            }

            _filterOptionsLoaded = true;
        }
        catch (Exception ex)
        {
            // Non-fatal: the panel still works with free-text search; just log it.
            Debug.WriteLine($"[SearchStationViewModel] Failed to load filter options: {ex.Message}");
        }
        finally
        {
            IsLoadingFilterOptions = false;
        }
    }

    /// <summary>
    /// Clears every filter selection. Each reset kicks off a search, but PerformSearchAsync
    /// debounces and cancels the prior run, so the net effect is a single refreshed search.
    /// </summary>
    public void ClearFilters()
    {
        SelectedCountry = null;
        SelectedLanguage = null;
        SelectedGenre = null;
        SelectedCodec = null;
        SelectedBitrate = null;
        HideBroken = false;
    }

    private async Task PerformSearchAsync()
    {
        // Cancel any ongoing search
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource = new CancellationTokenSource();

        // Wait a bit for the user to finish typing / adjusting filters
        try
        {
            await Task.Delay(500, _searchCancellationTokenSource.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        StationSearchQuery query = BuildQuery();

        // Nothing to search for: clear results and return to the initial state.
        if (query.IsEmpty)
        {
            SearchResults.Clear();
            HasError = false;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(ShowInitialState));
            return;
        }

        IsSearching = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            List<RadioBrowserStation> results = await _radioBrowserService.SearchAsync(
                query,
                _searchCancellationTokenSource.Token);

            SearchResults.Clear();
            foreach (RadioBrowserStation station in results)
            {
                SearchResults.Add(station);
            }

            OnPropertyChanged(nameof(ShowInitialState));
            Debug.WriteLine($"[SearchStationViewModel] Search completed. Found {SearchResults.Count} stations");
        }
        catch (TaskCanceledException)
        {
            // Search was cancelled, ignore
            Debug.WriteLine("[SearchStationViewModel] Search cancelled");
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[SearchStationViewModel] Network error: {ex.Message}");
            HasError = true;
            ErrorMessage = ex.StatusCode.HasValue
                ? $"Server error ({(int)ex.StatusCode}): The radio station service is temporarily unavailable. Please try again later."
                : "Network error: Unable to reach the radio station service. Please check your internet connection.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchStationViewModel] Search error: {ex.Message}");
            HasError = true;
            ErrorMessage = $"An error occurred while searching: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private StationSearchQuery BuildQuery()
    {
        return new StationSearchQuery
        {
            Name = SearchTerm,
            Country = SelectedCountry?.Name,
            Language = SelectedLanguage?.Name,
            Tags = SelectedGenre is null ? null : [SelectedGenre.Name],
            Codec = string.IsNullOrEmpty(SelectedCodec?.Value) ? null : SelectedCodec!.Value,
            BitrateMin = (SelectedBitrate?.Value ?? 0) > 0 ? SelectedBitrate!.Value : null,
            Order = SelectedSort.Order,
            Reverse = SelectedSort.Reverse,
            HideBroken = HideBroken,
            Limit = 50
        };
    }

    private void SetFilter<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ShowInitialState));
        _ = PerformSearchAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
