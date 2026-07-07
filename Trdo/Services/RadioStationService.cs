using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Trdo.Models;
using Windows.Storage;

namespace Trdo.Services;

public class RadioStationService
{
    private const string StationsKey = "RadioStations";
    private const string SelectedStationIndexKey = "SelectedStationIndex";

    private static readonly string _stationsFilePath =
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "stations.json");

    private static readonly Lazy<RadioStationService> _instance = new(() => new RadioStationService());
    public static RadioStationService Instance => _instance.Value;

    // Use source-generated JSON context to ensure trimming compatibility
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        TypeInfoResolver = RadioStationJsonContext.Default
    };

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
            string json = JsonSerializer.Serialize(stations.ToList(), _jsonOptions);
            File.WriteAllText(_stationsFilePath, json);
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
                return JsonSerializer.Deserialize<List<RadioStation>>(json, _jsonOptions) ?? [];
            }

            // One-time migration from LocalSettings
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(StationsKey, out object? value) &&
                value is string legacyJson)
            {
                List<RadioStation> migrated =
                    JsonSerializer.Deserialize<List<RadioStation>>(legacyJson, _jsonOptions) ?? [];
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
    /// Save the index of the selected station
    /// </summary>
    public void SaveSelectedStationIndex(int index)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SelectedStationIndexKey] = index;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving selected station index: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the index of the selected station
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
}
