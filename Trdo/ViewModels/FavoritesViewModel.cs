using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Trdo.ViewModels;

/// <summary>
/// The outcome of an export request, used to drive the result message on the Favorites page.
/// </summary>
public record FavoritesExportResult(bool Succeeded, int ExportedCount, int ArchivedCount, string FileName)
{
    public static FavoritesExportResult Cancelled { get; } = new(false, 0, 0, string.Empty);
}

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

    /// <summary>
    /// The number of favorites that have not been exported and archived yet.
    /// </summary>
    public int UnarchivedCount => Favorites.Count(f => !f.IsArchived);

    /// <summary>
    /// The number of favorites that have already been exported and archived.
    /// </summary>
    public int ArchivedCount => Favorites.Count(f => f.IsArchived);

    /// <summary>
    /// Indicates whether there is anything new to export.
    /// </summary>
    public bool HasUnarchivedFavorites => UnarchivedCount > 0;

    /// <summary>
    /// Indicates whether any favorite has been archived, which enables clearing export history.
    /// </summary>
    public bool HasArchivedFavorites => ArchivedCount > 0;

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
        OnPropertyChanged(nameof(UnarchivedCount));
        OnPropertyChanged(nameof(ArchivedCount));
        OnPropertyChanged(nameof(HasUnarchivedFavorites));
        OnPropertyChanged(nameof(HasArchivedFavorites));
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

    /// <summary>
    /// Exports favorites to a CSV or XSPF playlist file chosen by the user.
    /// </summary>
    /// <param name="windowHandle">Owner window handle for the save picker.</param>
    /// <param name="exportAll">When false, only favorites that have not been archived are exported.</param>
    /// <param name="archiveAfterExport">When true, exported tracks are stamped so they are skipped next time.</param>
    public async Task<FavoritesExportResult> ExportAsync(nint windowHandle, bool exportAll, bool archiveAfterExport)
    {
        List<FavoriteTrack> tracks = exportAll
            ? _favoritesService.GetFavorites()
            : _favoritesService.GetUnarchivedFavorites();

        if (tracks.Count == 0)
            return FavoritesExportResult.Cancelled;

        FileSavePicker picker = new();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        picker.SuggestedFileName = FavoriteTrackExportService.BuildSuggestedFileName();
        picker.FileTypeChoices.Add("CSV (streaming service import)", [FavoriteTrackExportService.CsvExtension]);
        picker.FileTypeChoices.Add("XSPF Playlist", [FavoriteTrackExportService.XspfExtension]);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
            return FavoritesExportResult.Cancelled;

        string extension = Path.GetExtension(file.Name);
        string content = FavoriteTrackExportService.Export(tracks, extension);

        await FileIO.WriteTextAsync(file, content);

        int archived = archiveAfterExport
            ? _favoritesService.MarkArchived(tracks.Select(t => t.Id))
            : 0;

        Debug.WriteLine($"[FavoritesViewModel] Exported {tracks.Count} favorites to {file.Name}, archived {archived}");
        return new FavoritesExportResult(true, tracks.Count, archived, file.Name);
    }

    /// <summary>
    /// Clears the archived flag on a single favorite so it is included in the next export.
    /// </summary>
    public void ClearArchived(FavoriteTrack? track)
    {
        if (track is null)
            return;

        _favoritesService.ClearArchived(track.Id);
    }

    /// <summary>
    /// Clears the archived flag on every favorite so they are all exported again.
    /// </summary>
    public int ClearAllArchived()
    {
        return _favoritesService.ClearAllArchived();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
