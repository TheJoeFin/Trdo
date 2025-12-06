using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Trdo.Models;
using Trdo.Services;

namespace Trdo.ViewModels;

/// <summary>
/// ViewModel for the Favorites page.
/// </summary>
public class FavoritesViewModel : INotifyPropertyChanged
{
    private readonly FavoritesService _favoritesService = FavoritesService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The collection of favorite tracks.
    /// </summary>
    public ObservableCollection<FavoriteTrack> Favorites { get; } = [];

    /// <summary>
    /// Indicates whether there are any favorites.
    /// </summary>
    public bool HasFavorites => Favorites.Count > 0;

    public FavoritesViewModel()
    {
        LoadFavorites();

        // Subscribe to changes
        _favoritesService.FavoritesChanged += (_, _) =>
        {
            LoadFavorites();
        };
    }

    private void LoadFavorites()
    {
        Favorites.Clear();
        foreach (FavoriteTrack favorite in _favoritesService.GetFavorites())
        {
            Favorites.Add(favorite);
        }
        OnPropertyChanged(nameof(HasFavorites));
        Debug.WriteLine($"[FavoritesViewModel] Loaded {Favorites.Count} favorites");
    }

    /// <summary>
    /// Removes a track from favorites.
    /// </summary>
    public void RemoveFavorite(FavoriteTrack? track)
    {
        if (track == null)
            return;

        Debug.WriteLine($"[FavoritesViewModel] Removing favorite: {track.DisplayText}");
        _favoritesService.RemoveFavorite(track.Id);
        // The FavoritesChanged event will trigger LoadFavorites
    }

    /// <summary>
    /// Removes a favorite by ID.
    /// </summary>
    public void RemoveFavoriteById(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        Debug.WriteLine($"[FavoritesViewModel] Removing favorite by ID: {id}");
        _favoritesService.RemoveFavorite(id);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
