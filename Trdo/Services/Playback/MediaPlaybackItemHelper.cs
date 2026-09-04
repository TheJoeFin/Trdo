using Windows.Media.Core;
using Windows.Media.Playback;

namespace Trdo.Services.Playback;

internal static class MediaPlaybackItemHelper
{
    public static void DisposePlayerSource(object? source)
    {
        switch (source)
        {
            case MediaPlaybackItem playbackItem:
                if (playbackItem.Source is MediaSource mediaSource)
                {
                    mediaSource.Reset();
                    mediaSource.Dispose();
                }

                break;
            case MediaSource directSource:
                directSource.Reset();
                directSource.Dispose();
                break;
        }
    }

    public static MediaSource? GetMediaSource(object? source) =>
        source switch
        {
            MediaPlaybackItem item => item.Source,
            MediaSource mediaSource => mediaSource,
            _ => null
        };

    public static MediaPlaybackItem? GetPlaybackItem(object? source) =>
        source as MediaPlaybackItem;
}
