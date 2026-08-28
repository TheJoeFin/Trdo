using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Trdo.Models;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Trdo.Services.Metadata;

/// <summary>
/// Reads HLS timed metadata (ID3 and emsg) from a MediaPlaybackItem.
/// </summary>
public sealed class NativeTimedMetadataService : IDisposable
{
    private const string Id3DispatchTypeGuid = "15260DFFFF49443320FF49443320000F";
    private const string EmsgDispatchType = "emsg:mp4";

    private readonly DispatcherQueue? _uiQueue;
    private MediaPlaybackItem? _attachedItem;
    private StreamMetadata _currentMetadata = StreamMetadata.Empty;

    public event EventHandler<StreamMetadata>? MetadataChanged;

    public StreamMetadata CurrentMetadata => _currentMetadata;

    public NativeTimedMetadataService(DispatcherQueue? uiQueue)
    {
        _uiQueue = uiQueue;
    }

    public void Attach(MediaPlaybackItem? playbackItem)
    {
        Detach(clearMetadata: false);

        if (playbackItem is null)
        {
            return;
        }

        _attachedItem = playbackItem;
        playbackItem.TimedMetadataTracksChanged += OnTimedMetadataTracksChanged;

        for (int index = 0; index < playbackItem.TimedMetadataTracks.Count; index++)
        {
            RegisterMetadataTrack(playbackItem, index);
        }
    }

    public void Detach(bool clearMetadata = true)
    {
        if (_attachedItem is null)
        {
            return;
        }

        _attachedItem.TimedMetadataTracksChanged -= OnTimedMetadataTracksChanged;
        _attachedItem = null;

        if (clearMetadata)
        {
            UpdateMetadata(StreamMetadata.Empty);
        }
    }

    private void OnTimedMetadataTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args)
    {
        if (args.CollectionChange == CollectionChange.ItemInserted)
        {
            RegisterMetadataTrack(sender, (int)args.Index);
        }
        else if (args.CollectionChange == CollectionChange.Reset)
        {
            for (int index = 0; index < sender.TimedMetadataTracks.Count; index++)
            {
                RegisterMetadataTrack(sender, index);
            }
        }
    }

    private void RegisterMetadataTrack(MediaPlaybackItem item, int index)
    {
        TimedMetadataTrack track = item.TimedMetadataTracks[index];
        string dispatchType = track.DispatchType ?? string.Empty;

        if (dispatchType.Contains(Id3DispatchTypeGuid, StringComparison.OrdinalIgnoreCase))
        {
            track.CueEntered -= OnId3CueEntered;
            track.CueEntered += OnId3CueEntered;
            item.TimedMetadataTracks.SetPresentationMode((uint)index, TimedMetadataTrackPresentationMode.ApplicationPresented);
            Debug.WriteLine("[NativeTimedMetadataService] Registered ID3 metadata track");
            return;
        }

        if (string.Equals(dispatchType, EmsgDispatchType, StringComparison.OrdinalIgnoreCase))
        {
            track.CueEntered -= OnEmsgCueEntered;
            track.CueEntered += OnEmsgCueEntered;
            item.TimedMetadataTracks.SetPresentationMode((uint)index, TimedMetadataTrackPresentationMode.ApplicationPresented);
            Debug.WriteLine("[NativeTimedMetadataService] Registered emsg metadata track");
        }
    }

    private void OnId3CueEntered(TimedMetadataTrack track, MediaCueEventArgs args)
    {
        if (args.Cue is not DataCue dataCue || dataCue.Data is null || dataCue.Data.Length == 0)
        {
            return;
        }

        byte[] bytes = BufferToArray(dataCue.Data);
        StreamMetadata metadata = Id3TagParser.Parse(bytes);
        if (metadata.HasMetadata)
        {
            UpdateMetadata(metadata);
        }
    }

    private void OnEmsgCueEntered(TimedMetadataTrack track, MediaCueEventArgs args)
    {
        if (args.Cue is not DataCue dataCue)
        {
            return;
        }

        StreamMetadata metadata = ParseEmsgCue(dataCue);
        if (metadata.HasMetadata)
        {
            UpdateMetadata(metadata);
        }
    }

    private static StreamMetadata ParseEmsgCue(DataCue dataCue)
    {
        StreamMetadata metadata = new();

        string scheme = GetCueProperty(dataCue, "emsg:scheme_id_uri");
        string value = GetCueProperty(dataCue, "emsg:value");

        if (!string.IsNullOrWhiteSpace(value))
        {
            TryApplyTextMetadata(value, metadata);
        }

        if (dataCue.Data is not null && dataCue.Data.Length > 0)
        {
            string payload = ReadCuePayload(dataCue.Data);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                TryApplyTextMetadata(payload, metadata);
            }
        }

        if (!metadata.HasMetadata && !string.IsNullOrWhiteSpace(scheme))
        {
            metadata.StreamTitle = scheme;
        }

        return metadata;
    }

    private static void TryApplyTextMetadata(string text, StreamMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(trimmed);
                JsonElement root = doc.RootElement;
                metadata.Artist = GetJsonString(root, "artist", "Artist", "TPE1") ?? string.Empty;
                metadata.Title = GetJsonString(root, "title", "Title", "TIT2", "song", "track") ?? string.Empty;
                metadata.AlbumArtUrl = GetJsonString(root, "artwork", "artworkURL", "albumArtUrl", "image");
                if (metadata.HasMetadata)
                {
                    metadata.StreamTitle = metadata.DisplayText;
                    return;
                }
            }
            catch (JsonException)
            {
                // fall through to plain text parsing
            }
        }

        Match match = Regex.Match(trimmed, @"^(?<artist>.+?)\s[-–—]\s(?<title>.+)$");
        if (match.Success)
        {
            metadata.Artist = match.Groups["artist"].Value.Trim();
            metadata.Title = match.Groups["title"].Value.Trim();
            metadata.StreamTitle = trimmed;
            return;
        }

        metadata.StreamTitle = trimmed;
        metadata.Title = trimmed;
    }

    private static string? GetJsonString(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string GetCueProperty(DataCue cue, string key)
    {
        cue.Properties.TryGetValue(key, out object? value);
        return value?.ToString() ?? string.Empty;
    }

    private static string ReadCuePayload(IBuffer buffer)
    {
        DataReader reader = DataReader.FromBuffer(buffer);
        reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
        uint length = buffer.Length;
        if (length == 0)
        {
            return string.Empty;
        }

        return reader.ReadString(length);
    }

    private static byte[] BufferToArray(IBuffer buffer)
    {
        if (buffer.Length == 0)
        {
            return [];
        }

        byte[] bytes = new byte[buffer.Length];
        DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
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
        Debug.WriteLine($"[NativeTimedMetadataService] Metadata updated: {metadata.DisplayText}");

        void Raise() => MetadataChanged?.Invoke(this, metadata);
        if (_uiQueue is null || _uiQueue.HasThreadAccess)
        {
            Raise();
        }
        else
        {
            _uiQueue.TryEnqueue(Raise);
        }
    }

    public void Dispose()
    {
        Detach();
    }
}
