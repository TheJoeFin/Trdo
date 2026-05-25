using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Trdo.Models;

namespace Trdo.Services.Metadata;

/// <summary>
/// Lightweight ID3v2.3/v2.4 parser for HLS timed metadata payloads.
/// </summary>
internal static class Id3TagParser
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    public static StreamMetadata Parse(byte[] id3Data)
    {
        StreamMetadata metadata = new();

        if (id3Data.Length < 10)
        {
            return metadata;
        }

        string header = Latin1.GetString(id3Data, 0, 3);
        if (!header.Equals("ID3", StringComparison.Ordinal))
        {
            return metadata;
        }

        bool isV4 = id3Data[3] == 4;
        int tagSize = ReadSyncSafeInt(id3Data.AsSpan(6, 4));
        int offset = 10;
        int end = Math.Min(id3Data.Length, 10 + tagSize);

        while (offset + 10 <= end)
        {
            string frameId = Latin1.GetString(id3Data, offset, 4);
            if (string.IsNullOrWhiteSpace(frameId) || frameId == "\0\0\0\0")
            {
                break;
            }

            int frameSize = isV4
                ? ReadSyncSafeInt(id3Data.AsSpan(offset + 4, 4))
                : (id3Data[offset + 4] << 24) |
                  (id3Data[offset + 5] << 16) |
                  (id3Data[offset + 6] << 8) |
                  id3Data[offset + 7];

            int frameHeaderSize = isV4 ? 10 : 10;
            int frameDataOffset = offset + frameHeaderSize;
            if (frameSize <= 0 || frameDataOffset + frameSize > id3Data.Length)
            {
                break;
            }

            ReadOnlySpan<byte> frameData = id3Data.AsSpan(frameDataOffset, frameSize);
            ApplyFrame(frameId, frameData, metadata);
            offset = frameDataOffset + frameSize;
        }

        if (metadata.HasMetadata && string.IsNullOrWhiteSpace(metadata.StreamTitle))
        {
            metadata.StreamTitle = metadata.DisplayText;
        }

        return metadata;
    }

    private static void ApplyFrame(string frameId, ReadOnlySpan<byte> frameData, StreamMetadata metadata)
    {
        switch (frameId)
        {
            case "TIT2":
                metadata.Title = ReadTextFrame(frameData);
                break;
            case "TPE1":
                metadata.Artist = ReadTextFrame(frameData);
                break;
            case "TALB":
                if (string.IsNullOrWhiteSpace(metadata.StreamTitle))
                {
                    metadata.StreamTitle = ReadTextFrame(frameData);
                }

                break;
            case "WXXX":
            case "WCOM":
            case "WOAF":
                string? url = ReadUrlFrame(frameData);
                if (!string.IsNullOrWhiteSpace(url) && LooksLikeImageUrl(url))
                {
                    metadata.AlbumArtUrl = url;
                }

                break;
            case "TXXX":
                ParseTxxxFrame(frameData, metadata);
                break;
            case "APIC":
                ParseApicFrame(frameData, metadata);
                break;
        }
    }

    private static void ParseTxxxFrame(ReadOnlySpan<byte> frameData, StreamMetadata metadata)
    {
        if (frameData.Length < 2)
        {
            return;
        }

        int index = 1;
        string description = ReadNullTerminatedLatin1(frameData, ref index);
        string value = ReadRemainingUtf8OrLatin1(frameData, index);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (description.Contains("artist", StringComparison.OrdinalIgnoreCase))
        {
            metadata.Artist = value;
        }
        else if (description.Contains("title", StringComparison.OrdinalIgnoreCase))
        {
            metadata.Title = value;
        }
        else if (description.Contains("art", StringComparison.OrdinalIgnoreCase) && LooksLikeImageUrl(value))
        {
            metadata.AlbumArtUrl = value;
        }
    }

    private static void ParseApicFrame(ReadOnlySpan<byte> frameData, StreamMetadata metadata)
    {
        if (frameData.Length < 5)
        {
            return;
        }

        int index = 1;
        ReadNullTerminatedLatin1(frameData, ref index);
        ReadNullTerminatedLatin1(frameData, ref index);

        if (index >= frameData.Length)
        {
            return;
        }

        // APIC image bytes are embedded; expose via data URL for downstream SMTC handling.
        byte[] imageBytes = frameData[index..].ToArray();
        if (imageBytes.Length == 0)
        {
            return;
        }

        string mime = DetectImageMime(imageBytes);
        metadata.AlbumArtUrl = $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";
    }

    private static string ReadTextFrame(ReadOnlySpan<byte> frameData)
    {
        if (frameData.IsEmpty)
        {
            return string.Empty;
        }

        int index = 0;
        return ReadEncodedString(frameData, ref index);
    }

    private static string? ReadUrlFrame(ReadOnlySpan<byte> frameData)
    {
        if (frameData.Length < 2)
        {
            return null;
        }

        int index = 1;
        return ReadRemainingUtf8OrLatin1(frameData, index).Trim();
    }

    private static string ReadEncodedString(ReadOnlySpan<byte> data, ref int index)
    {
        if (index >= data.Length)
        {
            return string.Empty;
        }

        byte encoding = data[index++];
        ReadOnlySpan<byte> textSpan = data[index..];

        string text = encoding switch
        {
            0 => Latin1.GetString(textSpan).TrimEnd('\0'),
            1 => Encoding.Unicode.GetString(textSpan).TrimEnd('\0'),
            2 => Encoding.BigEndianUnicode.GetString(textSpan).TrimEnd('\0'),
            3 => Encoding.UTF8.GetString(textSpan).TrimEnd('\0'),
            _ => Latin1.GetString(textSpan).TrimEnd('\0')
        };

        return text.Trim();
    }

    private static string ReadNullTerminatedLatin1(ReadOnlySpan<byte> data, ref int index)
    {
        int start = index;
        while (index < data.Length && data[index] != 0)
        {
            index++;
        }

        string value = Latin1.GetString(data.Slice(start, index - start));
        if (index < data.Length)
        {
            index++;
        }

        return value;
    }

    private static string ReadRemainingUtf8OrLatin1(ReadOnlySpan<byte> data, int index)
    {
        if (index >= data.Length)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> span = data[index..];
        string utf8 = Encoding.UTF8.GetString(span).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(utf8) ? Latin1.GetString(span).TrimEnd('\0') : utf8;
    }

    private static int ReadSyncSafeInt(ReadOnlySpan<byte> data)
    {
        return (data[0] << 21) | (data[1] << 14) | (data[2] << 7) | data[3];
    }

    private static bool LooksLikeImageUrl(string url)
    {
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string lower = url.ToLowerInvariant();
        return lower.Contains(".jpg") ||
               lower.Contains(".jpeg") ||
               lower.Contains(".png") ||
               lower.Contains(".webp") ||
               lower.Contains(".gif");
    }

    private static string DetectImageMime(byte[] imageBytes)
    {
        if (imageBytes.Length >= 3 &&
            imageBytes[0] == 0xFF &&
            imageBytes[1] == 0xD8 &&
            imageBytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (imageBytes.Length >= 8 &&
            imageBytes[0] == 0x89 &&
            imageBytes[1] == 0x50 &&
            imageBytes[2] == 0x4E &&
            imageBytes[3] == 0x47)
        {
            return "image/png";
        }

        return "image/jpeg";
    }
}
