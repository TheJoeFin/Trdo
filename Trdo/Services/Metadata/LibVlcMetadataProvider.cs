using System;
using System.Diagnostics;
using LibVLCSharp.Shared;
using Trdo.Models;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Trdo.Services.Metadata;

/// <summary>
/// Reads now-playing metadata from LibVLC media meta and parsed events.
/// </summary>
public sealed class LibVlcMetadataProvider : IDisposable
{
    private VlcMediaPlayer? _mediaPlayer;
    private StreamMetadata _currentMetadata = StreamMetadata.Empty;
    private DateTime _lastReadUtc = DateTime.MinValue;

    public event EventHandler<StreamMetadata>? MetadataChanged;

    public StreamMetadata CurrentMetadata => _currentMetadata;

    public void Attach(VlcMediaPlayer mediaPlayer)
    {
        Detach(clearMetadata: false);
        _mediaPlayer = mediaPlayer;
        _mediaPlayer.MediaChanged += OnMediaChanged;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.TimeChanged += OnTimeChanged;
    }

    public void Detach(bool clearMetadata = true)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.MediaChanged -= OnMediaChanged;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer = null;

        if (clearMetadata)
        {
            UpdateMetadata(StreamMetadata.Empty);
        }
    }

    private void OnPlaying(object? sender, EventArgs e) => ReadMetadataFromMedia(force: true);

    private void OnMediaChanged(object? sender, MediaPlayerMediaChangedEventArgs e) =>
        ReadMetadataFromMedia(force: true);

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (DateTime.UtcNow - _lastReadUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        ReadMetadataFromMedia(force: false);
    }

    private void ReadMetadataFromMedia(bool force)
    {
        if (_mediaPlayer?.Media is null)
        {
            return;
        }

        _lastReadUtc = DateTime.UtcNow;
        Media media = _mediaPlayer.Media;
        StreamMetadata metadata = new()
        {
            Title = media.Meta(MetadataType.Title) ?? string.Empty,
            Artist = media.Meta(MetadataType.Artist) ?? string.Empty,
            StreamTitle = media.Meta(MetadataType.NowPlaying) ?? string.Empty,
            AlbumArtUrl = media.Meta(MetadataType.ArtworkURL)
        };

        if (string.IsNullOrWhiteSpace(metadata.StreamTitle))
        {
            metadata.StreamTitle = metadata.DisplayText;
        }

        if (metadata.HasMetadata)
        {
            UpdateMetadata(metadata);
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
        Debug.WriteLine($"[LibVlcMetadataProvider] Metadata updated: {metadata.DisplayText}");
        MetadataChanged?.Invoke(this, metadata);
    }

    public void Dispose()
    {
        Detach();
    }

    public void Refresh()
    {
        ReadMetadataFromMedia(force: true);
    }
}
