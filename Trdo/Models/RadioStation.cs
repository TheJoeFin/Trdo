using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trdo.Models;

public partial class RadioStation : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _streamUrl = string.Empty;
    private string? _homepage;
    private string? _faviconUrl;
    // Default to 100% so stations loaded from older data (no volume field) are not silent.
    private double _volume = 1.0;
    private double? _bufferLevel;
    private double? _songPopupDelaySeconds;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Name
    {
        get => _name;
        set
        {
            if (value == _name) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public required string StreamUrl
    {
        get => _streamUrl;
        set
        {
            if (value == _streamUrl) return;
            _streamUrl = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The homepage URL of the radio station, if available.
    /// </summary>
    public string? Homepage
    {
        get => _homepage;
        set
        {
            if (value == _homepage) return;
            _homepage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The URL to the station's favicon/logo image, if available.
    /// </summary>
    public string? FaviconUrl
    {
        get => _faviconUrl;
        set
        {
            if (value == _faviconUrl) return;
            _faviconUrl = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station playback volume as a fraction where 1.0 == 100% of the stream
    /// volume. Values above 1.0 (up to 2.0) amplify the stream on the LibVLC engine.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = System.Math.Clamp(value, 0, 2);
            if (value == _volume) return;
            _volume = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station override for the buffer level (0-3), or <c>null</c> to follow
    /// the app-wide buffer setting. Stations that stream fine at the default do
    /// not need to pay for a station that only behaves with a large buffer.
    /// <para>
    /// This is a floor, exactly like the global setting: the watchdog's transient
    /// auto-bump still stacks on top when it detects stutter. Stations saved
    /// before this existed have no value and so follow the global setting.
    /// </para>
    /// </summary>
    public double? BufferLevel
    {
        get => _bufferLevel;
        set
        {
            double? clamped = value is null ? null : System.Math.Clamp(value.Value, 0, 3);
            if (clamped == _bufferLevel) return;
            _bufferLevel = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station override for how long to wait after a metadata change before showing
    /// the song change popup, in seconds, or <c>null</c> to follow the app-wide setting.
    /// <para>
    /// Stations differ in how far ahead of the audio their encoder announces a track, so
    /// this is a per-station property in practice: a delay that lines the popup up on one
    /// station makes it late on another. Stations saved before this existed have no value
    /// and so follow the global setting.
    /// </para>
    /// </summary>
    public double? SongPopupDelaySeconds
    {
        get => _songPopupDelaySeconds;
        set
        {
            double? clamped = value is null
                ? null
                : Services.SongChangeAnnouncementPolicy.ClampDelay(value.Value);
            if (clamped == _songPopupDelaySeconds) return;
            _songPopupDelaySeconds = clamped;
            OnPropertyChanged();
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
