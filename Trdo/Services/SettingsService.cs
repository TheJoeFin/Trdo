using Windows.Storage;

namespace Trdo.Services;

/// <summary>
/// Centralized service for managing application settings
/// </summary>
public static class SettingsService
{
    private const string IsFirstRunKey = "IsFirstRun";

    /// <summary>
    /// Gets whether this is the first run of the application
    /// </summary>
    public static bool IsFirstRun
    {
        get
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsFirstRunKey, out object? value))
                {
                    return value switch
                    {
                        bool b => b,
                        string s when bool.TryParse(s, out bool b2) => b2,
                        _ => true // Default to true if value is unexpected
                    };
                }
                // If key doesn't exist, it's the first run
                return true;
            }
            catch
            {
                // If any error occurs, default to true
                return true;
            }
        }
    }

    /// <summary>
    /// Marks that the first run has been completed
    /// </summary>
    public static void MarkFirstRunComplete()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[IsFirstRunKey] = false;
        }
        catch
        {
            // Silently fail if unable to save
        }
    }
}
