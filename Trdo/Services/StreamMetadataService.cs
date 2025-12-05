using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// Service for extracting metadata from internet radio streams using the ICY (Icecast/Shoutcast) protocol.
/// </summary>
public sealed class StreamMetadataService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly object _pollingLock = new();
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string? _currentStreamUrl;
    private StreamMetadata _currentMetadata = StreamMetadata.Empty;
    private bool _isDisposed;

    /// <summary>
    /// Polling interval for metadata updates.
    /// </summary>
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Event raised when stream metadata changes.
    /// </summary>
    public event EventHandler<StreamMetadata>? MetadataChanged;

    /// <summary>
    /// Gets the current stream metadata.
    /// </summary>
    public StreamMetadata CurrentMetadata => _currentMetadata;

    public StreamMetadataService()
    {
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            // Short timeout for metadata requests since we only need headers
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // Request ICY metadata by adding the required header
        _httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "1");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Trdo/1.0");
    }

    /// <summary>
    /// Starts polling for metadata from the specified stream URL.
    /// </summary>
    public void StartPolling(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            Debug.WriteLine("[StreamMetadataService] Cannot start polling with empty URL");
            return;
        }

        lock (_pollingLock)
        {
            // Stop any existing polling
            StopPollingCore();

            _currentStreamUrl = streamUrl;
            _pollingCts = new CancellationTokenSource();

            Debug.WriteLine($"[StreamMetadataService] Starting metadata polling for: {streamUrl}");

            CancellationToken token = _pollingCts.Token;
            _pollingTask = Task.Run(async () =>
            {
                try
                {
                    // Fetch initial metadata immediately
                    await FetchMetadataAsync(token);

                    // Then poll periodically
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(_pollingInterval, token);
                            await FetchMetadataAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[StreamMetadataService] Polling error: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Task was cancelled, this is expected during disposal
                    Debug.WriteLine("[StreamMetadataService] Polling task cancelled");
                }
                catch (Exception ex)
                {
                    // Log any unhandled exceptions to prevent unobserved task exceptions
                    Debug.WriteLine($"[StreamMetadataService] Unhandled polling error: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Stops polling for metadata.
    /// </summary>
    public void StopPolling()
    {
        lock (_pollingLock)
        {
            StopPollingCore();
        }
    }

    /// <summary>
    /// Core stop logic - must be called under lock.
    /// </summary>
    private void StopPollingCore()
    {
        Debug.WriteLine("[StreamMetadataService] Stopping metadata polling");
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
        _currentStreamUrl = null;

        // Clear metadata when stopping
        UpdateMetadata(StreamMetadata.Empty);
    }

    /// <summary>
    /// Fetches metadata from the current stream URL.
    /// </summary>
    private async Task FetchMetadataAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentStreamUrl))
        {
            return;
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, _currentStreamUrl);

            // Request only partial content to minimize bandwidth
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[StreamMetadataService] HTTP {(int)response.StatusCode} from stream");
                return;
            }

            // Check for ICY metadata interval header in response headers and content headers
            int metaInterval = GetIcyMetaInterval(response);
            Debug.WriteLine($"[StreamMetadataService] icy-metaint: {metaInterval}");

            if (metaInterval <= 0)
            {
                // No ICY metadata support
                Debug.WriteLine("[StreamMetadataService] Stream does not support ICY metadata");
                return;
            }

            // Read stream data to find metadata
            // Using ResponseHeadersRead allows us to start processing the stream immediately
            // The 'using' statement ensures the stream is disposed after reading just enough data
            // to extract the first metadata block, minimizing bandwidth usage
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            StreamMetadata? metadata = await ReadIcyMetadataAsync(stream, metaInterval, cancellationToken);

            if (metadata is not null && metadata.HasMetadata)
            {
                UpdateMetadata(metadata);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[StreamMetadataService] HTTP error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StreamMetadataService] Error fetching metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads ICY metadata from the stream.
    /// </summary>
    private static async Task<StreamMetadata?> ReadIcyMetadataAsync(
        Stream stream,
        int metaInterval,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read and discard audio data in chunks until we reach the metadata
            // Using a fixed buffer size reduces memory allocation for large metaInterval values
            const int chunkSize = 8192;
            byte[] discardBuffer = new byte[Math.Min(chunkSize, metaInterval)];
            int totalRead = 0;

            while (totalRead < metaInterval)
            {
                int bytesToRead = Math.Min(discardBuffer.Length, metaInterval - totalRead);
                int bytesRead = await stream.ReadAsync(
                    discardBuffer.AsMemory(0, bytesToRead),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    Debug.WriteLine("[StreamMetadataService] Stream ended before metadata");
                    return null;
                }

                totalRead += bytesRead;
            }

            // Read metadata length byte (multiply by 16 to get actual length)
            // Reuse the first byte of the discard buffer for efficiency
            int lengthBytesRead = await stream.ReadAsync(discardBuffer.AsMemory(0, 1), cancellationToken);
            if (lengthBytesRead == 0)
            {
                Debug.WriteLine("[StreamMetadataService] Could not read metadata length");
                return null;
            }

            int metaLength = discardBuffer[0] * 16;
            if (metaLength == 0)
            {
                // No metadata in this block
                Debug.WriteLine("[StreamMetadataService] No metadata in current block");
                return null;
            }

            // Read metadata
            byte[] metaBuffer = new byte[metaLength];
            totalRead = 0;

            while (totalRead < metaLength)
            {
                int bytesRead = await stream.ReadAsync(
                    metaBuffer.AsMemory(totalRead, metaLength - totalRead),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    Debug.WriteLine("[StreamMetadataService] Stream ended before full metadata");
                    return null;
                }

                totalRead += bytesRead;
            }

            // Parse metadata string (null-terminated, padded with zeros)
            string metadataStr = Encoding.UTF8.GetString(metaBuffer).TrimEnd('\0');
            Debug.WriteLine($"[StreamMetadataService] Raw metadata: {metadataStr}");

            return ParseIcyMetadata(metadataStr);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StreamMetadataService] Error reading ICY metadata: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses an ICY metadata string into a StreamMetadata object.
    /// Format is typically: StreamTitle='Artist - Song';StreamUrl='...';
    /// </summary>
    private static StreamMetadata ParseIcyMetadata(string metadataStr)
    {
        StreamMetadata metadata = new();

        if (string.IsNullOrWhiteSpace(metadataStr))
        {
            return metadata;
        }

        // Extract StreamTitle
        const string streamTitleKey = "StreamTitle='";
        int titleStart = metadataStr.IndexOf(streamTitleKey, StringComparison.OrdinalIgnoreCase);

        if (titleStart >= 0)
        {
            titleStart += streamTitleKey.Length;
            int titleEnd = metadataStr.IndexOf("';", titleStart, StringComparison.Ordinal);

            if (titleEnd > titleStart)
            {
                metadata.StreamTitle = metadataStr[titleStart..titleEnd];
                Debug.WriteLine($"[StreamMetadataService] StreamTitle: {metadata.StreamTitle}");

                // Try to parse "Artist - Title" format
                ParseArtistAndTitle(metadata);
            }
        }

        return metadata;
    }

    /// <summary>
    /// Attempts to parse Artist and Title from the StreamTitle using common formats.
    /// </summary>
    private static void ParseArtistAndTitle(StreamMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.StreamTitle))
        {
            return;
        }

        // Common separators: " - ", " – ", " — "
        string[] separators = [" - ", " – ", " — "];

        foreach (string separator in separators)
        {
            int separatorIndex = metadata.StreamTitle.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                metadata.Artist = metadata.StreamTitle[..separatorIndex].Trim();
                metadata.Title = metadata.StreamTitle[(separatorIndex + separator.Length)..].Trim();
                Debug.WriteLine($"[StreamMetadataService] Parsed - Artist: {metadata.Artist}, Title: {metadata.Title}");
                return;
            }
        }

        // If no separator found, use the whole string as Title
        metadata.Title = metadata.StreamTitle;
    }

    /// <summary>
    /// Extracts the ICY metadata interval from response headers.
    /// </summary>
    private static int GetIcyMetaInterval(HttpResponseMessage response)
    {
        // Check response headers first
        if (response.Headers.TryGetValues("icy-metaint", out var metaIntValues))
        {
            foreach (var metaIntStr in metaIntValues)
            {
                if (int.TryParse(metaIntStr, out int parsed))
                {
                    return parsed;
                }
            }
        }

        // Also check content headers (some servers send it there)
        if (response.Content.Headers.TryGetValues("icy-metaint", out var contentMetaIntValues))
        {
            foreach (var val in contentMetaIntValues)
            {
                if (int.TryParse(val, out int parsed))
                {
                    return parsed;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Updates the current metadata and raises the MetadataChanged event if changed.
    /// </summary>
    private void UpdateMetadata(StreamMetadata newMetadata)
    {
        // Check if metadata actually changed
        if (_currentMetadata.StreamTitle == newMetadata.StreamTitle &&
            _currentMetadata.Artist == newMetadata.Artist &&
            _currentMetadata.Title == newMetadata.Title)
        {
            return;
        }

        _currentMetadata = newMetadata;
        Debug.WriteLine($"[StreamMetadataService] Metadata updated: {newMetadata.DisplayText}");
        MetadataChanged?.Invoke(this, newMetadata);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopPolling();
        _httpClient.Dispose();
    }
}
