using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Trdo.Models;
using Trdo.ViewModels;

namespace Trdo.Services;

/// <summary>
/// Singleton service that manages playlist history persistence across page navigations.
/// History is maintained as long as the app is running and the stream is active.
/// </summary>
public class PlaylistHistoryService
{
    private const int MaxHistoryItems = 25;

    private static readonly Lazy<PlaylistHistoryService> _instance = new(() => new PlaylistHistoryService());
    public static PlaylistHistoryService Instance => _instance.Value;

    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private readonly FavoritesService _favoritesService = FavoritesService.Instance;

    /// <summary>
    /// The playlist history showing recent tracks (most recent first).
    /// </summary>
    public ObservableCollection<PlaylistHistoryItem> History { get; } = [];

    /// <summary>
    /// Event raised when the history changes.
    /// </summary>
    public event EventHandler? HistoryChanged;

    private PlaylistHistoryService()
    {
        // Subscribe to metadata changes from the player
        _player.StreamMetadataChanged += OnStreamMetadataChanged;

        // Subscribe to favorites changes to update history items
        _favoritesService.FavoritesChanged += OnFavoritesChanged;

        // Check if there's already metadata (stream started before this service was accessed)
        if (_player.CurrentMetadata?.HasMetadata == true)
        {
            Debug.WriteLine("[PlaylistHistoryService] Found existing metadata on init, adding to history");
            AddToHistory(_player.CurrentMetadata);
        }

        Debug.WriteLine("[PlaylistHistoryService] Initialized and subscribed to metadata changes");
    }

    /// <summary>
    /// Ensures the service is initialized. Call this early in app startup.
    /// </summary>
    public static void EnsureInitialized()
    {
        // Accessing Instance triggers the lazy initialization
        _ = Instance;
        Debug.WriteLine("[PlaylistHistoryService] EnsureInitialized called");
    }

    private void OnStreamMetadataChanged(object? sender, StreamMetadata metadata)
    {
        if (metadata?.HasMetadata != true)
            return;

        AddToHistory(metadata);
    }

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        // Refresh favorite status on all history items
        foreach (PlaylistHistoryItem item in History)
        {
            item.RefreshFavoriteStatus();
        }
    }

    /// <summary>
    /// Adds a track to the history.
    /// </summary>
    public void AddToHistory(StreamMetadata metadata)
    {
        if (metadata?.HasMetadata != true)
            return;

        string stationName = PlayerViewModel.Shared.SelectedStation?.Name ?? "Unknown Station";
        PlaylistHistoryItem newItem = PlaylistHistoryItem.FromMetadata(metadata, stationName);

        // Check if this track is already at the top of the history (avoid duplicates for same track)
        // This handles pause/resume within the same track
        if (History.Count > 0)
        {
            PlaylistHistoryItem topItem = History[0];
            if (topItem.UniqueKey == newItem.UniqueKey)
            {
                Debug.WriteLine("[PlaylistHistoryService] Track already at top of history, skipping duplicate");
                return;
            }
        }

        // Insert at the beginning (most recent first)
        History.Insert(0, newItem);
        Debug.WriteLine($"[PlaylistHistoryService] Added to history: {newItem.DisplayText} (Total: {History.Count})");

        // Trim history if it exceeds max
        while (History.Count > MaxHistoryItems)
        {
            History.RemoveAt(History.Count - 1);
            Debug.WriteLine($"[PlaylistHistoryService] Trimmed history to {MaxHistoryItems} items");
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all history.
    /// </summary>
    public void ClearHistory()
    {
        History.Clear();
        Debug.WriteLine("[PlaylistHistoryService] History cleared");
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
