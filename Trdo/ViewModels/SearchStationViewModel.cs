using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

public class SearchStationViewModel : INotifyPropertyChanged
{
    private string _searchTerm = string.Empty;
    private bool _isSearching;
    private readonly RadioBrowserService _radioBrowserService = new();
    private CancellationTokenSource? _searchCancellationTokenSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RadioBrowserStation> SearchResults { get; } = [];

    public bool ShowInitialState => string.IsNullOrWhiteSpace(SearchTerm) &&
                SearchResults.Count == 0 &&
                !IsSearching;

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (value == _searchTerm) return;
            _searchTerm = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInitialState));

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
            OnPropertyChanged(nameof(ShowInitialState));
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
            List<RadioBrowserStation> results = await _radioBrowserService.SearchByNameAsync(
                SearchTerm,
                limit: 50,
                cancellationToken: _searchCancellationTokenSource.Token);

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
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchStationViewModel] Search error: {ex.Message}");
            // Could add error handling here
        }
        finally
        {
            IsSearching = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
