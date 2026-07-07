using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Trdo.Models;
using Trdo.Services.Playback;

namespace Trdo.Services.Metadata;

/// <summary>
/// Polls HLS media playlists and extracts ID3 metadata embedded in the latest segment.
/// </summary>
public sealed class HlsSegmentMetadataService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly object _pollingLock = new();
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string? _currentPlaylistUrl;
    private StreamMetadata _currentMetadata = StreamMetadata.Empty;
    private bool _isDisposed;

    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

    public event EventHandler<StreamMetadata>? MetadataChanged;

    public StreamMetadata CurrentMetadata => _currentMetadata;

    public HlsSegmentMetadataService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Trdo/1.0");
    }

    public void StartPolling(string playlistUrl)
    {
        if (!HlsStreamHelper.IsLikelyHlsUrl(playlistUrl))
        {
            return;
        }

        lock (_pollingLock)
        {
            StopPollingCore(clearMetadata: false);

            _currentPlaylistUrl = playlistUrl;
            _pollingCts = new CancellationTokenSource();

            Debug.WriteLine($"[HlsSegmentMetadataService] Starting polling for: {playlistUrl}");

            CancellationToken token = _pollingCts.Token;
            _pollingTask = Task.Run(async () =>
            {
                try
                {
                    await FetchMetadataAsync(token);

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
                            Debug.WriteLine($"[HlsSegmentMetadataService] Polling error: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[HlsSegmentMetadataService] Polling task cancelled");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HlsSegmentMetadataService] Unhandled polling error: {ex.Message}");
                }
            });
        }
    }

    public void StopPolling(bool clearMetadata = true)
    {
        lock (_pollingLock)
        {
            StopPollingCore(clearMetadata);
        }
    }

    public async Task RefreshAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        if (!HlsStreamHelper.IsLikelyHlsUrl(playlistUrl))
        {
            return;
        }

        await FetchMetadataAsync(playlistUrl, cancellationToken);
    }

    private async Task FetchMetadataAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentPlaylistUrl))
        {
            return;
        }

        await FetchMetadataAsync(_currentPlaylistUrl, cancellationToken);
    }

    private async Task FetchMetadataAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        try
        {
            StreamMetadata? metadata = await HlsStreamHelper.TryFetchMetadataFromLatestSegmentAsync(
                _httpClient,
                playlistUrl,
                cancellationToken);

            if (metadata is not null && metadata.HasMetadata)
            {
                UpdateMetadata(metadata);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HlsSegmentMetadataService] Error fetching metadata: {ex.Message}");
        }
    }

    private void StopPollingCore(bool clearMetadata = true)
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
        _currentPlaylistUrl = null;

        if (clearMetadata)
        {
            UpdateMetadata(StreamMetadata.Empty);
        }
    }

    private void UpdateMetadata(StreamMetadata metadata)
    {
        if (_currentMetadata.StreamTitle == metadata.StreamTitle &&
            _currentMetadata.Artist == metadata.Artist &&
            _currentMetadata.Title == metadata.Title &&
            _currentMetadata.AlbumArtUrl == metadata.AlbumArtUrl)
        {
            return;
        }

        _currentMetadata = metadata;
        Debug.WriteLine($"[HlsSegmentMetadataService] Metadata updated: {metadata.DisplayText}");
        MetadataChanged?.Invoke(this, metadata);
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
