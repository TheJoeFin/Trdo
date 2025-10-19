using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Trdo.Models;
using Windows.Storage;

namespace Trdo.Services;

public class RadioStationService
{
    private const string StationsKey = "RadioStations";
    private const string SelectedStationIndexKey = "SelectedStationIndex";

    private static readonly Lazy<RadioStationService> _instance = new(() => new RadioStationService());
    public static RadioStationService Instance => _instance.Value;

    private RadioStationService()
    {
    }

    /// <summary>
    /// Save a list of radio stations to local settings
    /// </summary>
    public void SaveStations(IEnumerable<RadioStation> stations)
    {
        try
        {
            List<RadioStation> stationList = stations.ToList();
            var json = JsonSerializer.Serialize(stationList);
            ApplicationData.Current.LocalSettings.Values[StationsKey] = json;
        }
        catch (Exception ex)
        {
            // Log error in production
            System.Diagnostics.Debug.WriteLine($"Error saving stations: {ex.Message}");
        }
    }

    /// <summary>
    /// Load radio stations from local settings
    /// </summary>
    public List<RadioStation> LoadStations()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(StationsKey, out object? value) &&
                value is string json)
            {
                List<RadioStation>? stations = JsonSerializer.Deserialize<List<RadioStation>>(json);
                if (stations != null && stations.Count > 0)
                {
                    return stations;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error in production
            System.Diagnostics.Debug.WriteLine($"Error loading stations: {ex.Message}");
        }

        // Return default stations if loading fails or no stations exist
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
