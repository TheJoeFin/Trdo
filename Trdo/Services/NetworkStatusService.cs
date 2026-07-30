using System.Diagnostics;
using Windows.Networking.Connectivity;

namespace Trdo.Services;

/// <summary>
/// Lightweight helper for checking whether the machine currently has internet access.
/// Used to avoid attempting playback when offline and to describe network-related failures.
/// </summary>
public static class NetworkStatusService
{
    /// <summary>
    /// Returns true if the system reports an internet-capable connection profile.
    /// If connectivity can't be determined, returns true so we never block playback
    /// on a false negative.
    /// </summary>
    public static bool IsInternetAvailable()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                Debug.WriteLine("[NetworkStatusService] No internet connection profile found");
                return false;
            }

            NetworkConnectivityLevel level = profile.GetNetworkConnectivityLevel();
            bool hasInternet = level is NetworkConnectivityLevel.InternetAccess
                or NetworkConnectivityLevel.ConstrainedInternetAccess;
            Debug.WriteLine($"[NetworkStatusService] Connectivity level: {level}, hasInternet: {hasInternet}");
            return hasInternet;
        }
        catch (System.Exception ex)
        {
            // If the connectivity APIs throw, assume connected so we don't wrongly block playback.
            Debug.WriteLine($"[NetworkStatusService] Failed to determine connectivity, assuming available: {ex.Message}");
            return true;
        }
    }
}
