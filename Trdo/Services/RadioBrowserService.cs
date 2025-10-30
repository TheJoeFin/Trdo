using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Service for searching radio stations using the Radio Browser API
/// </summary>
public class RadioBrowserService
{
    private static readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://de1.api.radio-browser.info/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static RadioBrowserService()
    {
        // Set a user agent as recommended by Radio Browser API
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Trdo/1.0");
    }

    /// <summary>
    /// Searches for radio stations by name
    /// </summary>
    /// <param name="searchTerm">The search term (station name)</param>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching radio stations</returns>
    public async Task<List<RadioBrowserStation>> SearchByNameAsync(
        string searchTerm,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return [];
            }

            string encodedSearchTerm = Uri.EscapeDataString(searchTerm);
            string url = $"json/stations/byname/{encodedSearchTerm}?limit={limit}&order=votes&reverse=true";

            Debug.WriteLine($"[RadioBrowserService] Searching for: {searchTerm}");
            Debug.WriteLine($"[RadioBrowserService] URL: {_httpClient.BaseAddress}{url}");

            HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[RadioBrowserService] Received response, length: {content.Length}");

            List<RadioBrowserStation>? stations = JsonSerializer.Deserialize<List<RadioBrowserStation>>(content, _jsonOptions);

            Debug.WriteLine($"[RadioBrowserService] Found {stations?.Count ?? 0} stations");

            return stations ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioBrowserService] Error searching stations: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Searches for radio stations by tag/genre
    /// </summary>
    /// <param name="tag">The tag/genre to search for</param>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching radio stations</returns>
    public async Task<List<RadioBrowserStation>> SearchByTagAsync(
        string tag,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return [];
            }

            string encodedTag = Uri.EscapeDataString(tag);
            string url = $"json/stations/bytag/{encodedTag}?limit={limit}&order=votes&reverse=true";

            Debug.WriteLine($"[RadioBrowserService] Searching by tag: {tag}");

            HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            List<RadioBrowserStation>? stations = JsonSerializer.Deserialize<List<RadioBrowserStation>>(content, _jsonOptions);

            Debug.WriteLine($"[RadioBrowserService] Found {stations?.Count ?? 0} stations");

            return stations ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioBrowserService] Error searching by tag: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Searches for radio stations by country
    /// </summary>
    /// <param name="country">The country name or code</param>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching radio stations</returns>
    public async Task<List<RadioBrowserStation>> SearchByCountryAsync(
        string country,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                return [];
            }

            string encodedCountry = Uri.EscapeDataString(country);
            string url = $"json/stations/bycountry/{encodedCountry}?limit={limit}&order=votes&reverse=true";

            Debug.WriteLine($"[RadioBrowserService] Searching by country: {country}");

            HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            List<RadioBrowserStation>? stations = JsonSerializer.Deserialize<List<RadioBrowserStation>>(content, _jsonOptions);

            Debug.WriteLine($"[RadioBrowserService] Found {stations?.Count ?? 0} stations");

            return stations ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioBrowserService] Error searching by country: {ex.Message}");
            throw;
        }
    }
}
