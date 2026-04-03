using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.System.UserProfile;

namespace Trdo.Services;

internal static class MusicSearchLinkService
{
    private const string DefaultAppleMusicStorefront = "us";

    public static Uri CreateAppleMusicSearchUri(string searchText)
    {
        string storefront = GlobalizationPreferences.HomeGeographicRegion;
        if (string.IsNullOrWhiteSpace(storefront) || storefront.Length != 2)
        {
            storefront = DefaultAppleMusicStorefront;
        }

        string encodedSearchText = Uri.EscapeDataString(searchText);
        return new Uri($"https://music.apple.com/{storefront.ToLowerInvariant()}/search?term={encodedSearchText}");
    }

    public static async Task LaunchAppleMusicWebSearchAsync(string searchText)
    {
        Uri webSearchUri = CreateAppleMusicSearchUri(searchText);
        await Launcher.LaunchUriAsync(webSearchUri);
    }
}
