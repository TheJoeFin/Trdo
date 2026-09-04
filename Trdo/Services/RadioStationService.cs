using System;
using System.Collections.Generic;
using System.IO;
using Trdo.Models;
using Windows.Storage;

namespace Trdo.Services;

public class RadioStationService
{
    private const string StationsKey = "RadioStations";
    private const string SelectedStationIndexKey = "SelectedStationIndex";
    private const string SelectedStationIdKey = "SelectedStationId";

    private static readonly string _stationsFilePath =
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "stations.json");

    private static readonly string _layoutFilePath =
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "stationlayout.json");

    private static readonly Lazy<RadioStationService> _instance = new(() => new RadioStationService());
    public static RadioStationService Instance => _instance.Value;

    private RadioStationService()
    {
    }

    /// <summary>
    /// Save a list of radio stations to a JSON file in local app data
    /// </summary>
    public void SaveStations(IEnumerable<RadioStation> stations)
    {
        try
        {
            // Defensive: nothing without an id should ever reach disk, whatever route it
            // took to get into the list. Import adds stations to the collection directly
            // rather than through PlayerViewModel.AddStation, so this is not theoretical.
            StationIdentityPolicy.EnsureIds(stations);

            WriteFileAtomic(_stationsFilePath, StationStorageFormat.SerializeStations(stations));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving stations: {ex.Message}");
        }
    }

    /// <summary>
    /// Load radio stations from a JSON file, migrating from LocalSettings on first run
    /// </summary>
    public List<RadioStation> LoadStations()
    {
        try
        {
            if (File.Exists(_stationsFilePath))
            {
                string json = File.ReadAllText(_stationsFilePath);
                List<RadioStation> stations = StationStorageFormat.ParseStations(json);

                // Stations saved before ids existed - or written by an older build, which
                // drops the field - get one stamped now and written straight back, so the
                // layout file and the saved selection have something stable to point at.
                if (StationIdentityPolicy.EnsureIds(stations))
                {
                    System.Diagnostics.Debug.WriteLine("[RadioStationService] Stamped ids onto stations loaded without one");
                    SaveStations(stations);
                }

                return stations;
            }

            // One-time migration from LocalSettings
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(StationsKey, out object? value) &&
                value is string legacyJson)
            {
                List<RadioStation> migrated = StationStorageFormat.ParseStations(legacyJson);
                StationIdentityPolicy.EnsureIds(migrated);
                SaveStations(migrated);
                ApplicationData.Current.LocalSettings.Values.Remove(StationsKey);
                System.Diagnostics.Debug.WriteLine($"[RadioStationService] Migrated {migrated.Count} stations from LocalSettings to file");
                return migrated;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading stations: {ex.Message}");
        }

        return [];
    }

    /// <summary>
    /// Load the saved layout, or null when the user has never created a folder or divider.
    /// </summary>
    public StationLayoutDocument? LoadLayout()
    {
        try
        {
            if (File.Exists(_layoutFilePath))
                return StationStorageFormat.ParseLayout(File.ReadAllText(_layoutFilePath));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading station layout: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Save the layout. Call this <em>before</em> <see cref="SaveStations"/>: the two files
    /// cannot be written atomically together, and if only one lands it is better for the
    /// layout to reference a station set that is slightly stale than for a just-added station
    /// to be missing from the layout and so invisible until the next restart.
    /// </summary>
    public void SaveLayout(StationLayoutDocument document)
    {
        try
        {
            WriteFileAtomic(_layoutFilePath, StationStorageFormat.SerializeLayout(document));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving station layout: {ex.Message}");
        }
    }

    /// <summary>
    /// Save which station is selected, by id.
    /// <para>
    /// The legacy index is written alongside it. It costs one dictionary entry, and it means
    /// that rolling back to a build that only understands the index restores the station the
    /// user was actually listening to rather than the first one in the list.
    /// </para>
    /// </summary>
    public void SaveSelectedStation(string? stationId, int flattenedIndex)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SelectedStationIdKey] = stationId ?? string.Empty;
            ApplicationData.Current.LocalSettings.Values[SelectedStationIndexKey] = flattenedIndex;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving selected station: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the id of the selected station, or null if none has been saved yet.
    /// </summary>
    public string? LoadSelectedStationId()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SelectedStationIdKey, out object? value) &&
                value is string id && !string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading selected station id: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Load the index of the selected station. Still written by this build, and the only
    /// thing available on the first run after upgrading from a pre-2.0 version.
    /// </summary>
    public int LoadSelectedStationIndex()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SelectedStationIndexKey, out object? value))
            {
                return value switch
                {
                    int i => i,
                    string s when int.TryParse(s, out int i2) => i2,
                    _ => 0
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading selected station index: {ex.Message}");
        }

        return 0; // Default to first station
    }

    /// <summary>
    /// Writes via a temporary file and an atomic replace, so a crash or a power cut mid-write
    /// leaves the previous contents intact rather than a half-written file. Losing the whole
    /// station list to a torn write is not a recoverable error for the user.
    /// </summary>
    private static void WriteFileAtomic(string path, string contents)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
