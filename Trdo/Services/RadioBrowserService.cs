using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Describes a faceted radio station search against the Radio Browser API.
/// Any property left empty/null is omitted from the request.
/// </summary>
public class StationSearchQuery
{
    public string? Name { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public string? Codec { get; set; }
    public int? BitrateMin { get; set; }
    public string Order { get; set; } = "votes";
    public bool Reverse { get; set; } = true;
    public bool HideBroken { get; set; }
    public int Limit { get; set; } = 50;

    /// <summary>
    /// True when nothing but the default ordering is set, i.e. there is nothing to search for.
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) &&
        string.IsNullOrWhiteSpace(Country) &&
        string.IsNullOrWhiteSpace(Language) &&
        (Tags is null || Tags.Count == 0) &&
        string.IsNullOrWhiteSpace(Codec) &&
        !BitrateMin.HasValue;
}

/// <summary>
/// Service for searching radio stations using the Radio Browser API
/// </summary>
public class RadioBrowserService
{
    private static readonly HttpClient _httpClient = CreateHttpClient();

    // Use source-generated JSON context to ensure trimming compatibility
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = RadioBrowserJsonContext.Default
    };

    // Lookup lists rarely change, so fetch each one once and reuse it.
    private static List<RadioBrowserCountry>? _cachedCountries;
    private static List<RadioBrowserLanguage>? _cachedLanguages;
    private static List<RadioBrowserTag>? _cachedTags;

    private static HttpClient CreateHttpClient()
    {
        // Use SocketsHttpHandler to avoid DNS resolution issues in packaged apps
        // The WinINet-based handler can fail with error 8007277C in app containers
        SocketsHttpHandler handler = new()
        {
            // Disable proxy to ensure direct connection works in packaged apps
            UseProxy = false,
            // Enable automatic decompression for better performance
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://de1.api.radio-browser.info/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Set a user agent as recommended by Radio Browser API
        client.DefaultRequestHeaders.Add("User-Agent", "Trdo/1.0");

        return client;
    }

    /// <summary>
    /// Searches for radio stations using any combination of name and filters
    /// via the /json/stations/search endpoint.
    /// </summary>
    /// <param name="query">The search criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching radio stations</returns>
    public async Task<List<RadioBrowserStation>> SearchAsync(
        StationSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (query.IsEmpty)
            {
                return [];
            }

            StringBuilder url = new("json/stations/search?");
            AppendParam(url, "name", query.Name);
            AppendParam(url, "country", query.Country);
            AppendParam(url, "language", query.Language);
            if (query.Tags is { Count: > 0 })
            {
                AppendParam(url, "tagList", string.Join(",", query.Tags));
            }
            AppendParam(url, "codec", query.Codec);
            if (query.BitrateMin.HasValue)
            {
                AppendParam(url, "bitrateMin", query.BitrateMin.Value.ToString());
            }
            AppendParam(url, "order", query.Order);
            AppendParam(url, "reverse", query.Reverse ? "true" : "false");
            AppendParam(url, "hidebroken", query.HideBroken ? "true" : "false");
            AppendParam(url, "limit", query.Limit.ToString());

            string requestUrl = url.ToString().TrimEnd('&');

            Debug.WriteLine($"[RadioBrowserService] Search URL: {_httpClient.BaseAddress}{requestUrl}");

            HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);
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
            Debug.WriteLine($"[RadioBrowserService] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Gets the list of countries (ordered by station count, most first), cached after first fetch.
    /// </summary>
    public async Task<List<RadioBrowserCountry>> GetCountriesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedCountries is not null)
        {
            return _cachedCountries;
        }

        _cachedCountries = await GetListAsync<RadioBrowserCountry>(
            "json/countries?order=stationcount&reverse=true&hidebroken=true",
            cancellationToken);
        return _cachedCountries;
    }

    /// <summary>
    /// Gets the list of languages (ordered by station count, most first), cached after first fetch.
    /// </summary>
    public async Task<List<RadioBrowserLanguage>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedLanguages is not null)
        {
            return _cachedLanguages;
        }

        _cachedLanguages = await GetListAsync<RadioBrowserLanguage>(
            "json/languages?order=stationcount&reverse=true&hidebroken=true",
            cancellationToken);
        return _cachedLanguages;
    }

    /// <summary>
    /// Gets the most popular tags/genres (ordered by station count, most first), cached after first fetch.
    /// </summary>
    /// <param name="limit">Maximum number of tags to return (the full list is very large)</param>
    public async Task<List<RadioBrowserTag>> GetTagsAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        if (_cachedTags is not null)
        {
            return _cachedTags;
        }

        _cachedTags = await GetListAsync<RadioBrowserTag>(
            $"json/tags?order=stationcount&reverse=true&hidebroken=true&limit={limit}",
            cancellationToken);
        return _cachedTags;
    }

    private static async Task<List<T>> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            Debug.WriteLine($"[RadioBrowserService] Fetching list: {_httpClient.BaseAddress}{url}");

            HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            List<T>? items = JsonSerializer.Deserialize<List<T>>(content, _jsonOptions);

            Debug.WriteLine($"[RadioBrowserService] Fetched {items?.Count ?? 0} items");
            return items ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RadioBrowserService] Error fetching list: {ex.Message}");
            throw;
        }
    }

    private static void AppendParam(StringBuilder url, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        url.Append(key).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
    }
}
