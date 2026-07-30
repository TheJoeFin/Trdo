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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
