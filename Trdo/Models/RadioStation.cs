using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trdo.Models;

public partial class RadioStation : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _streamUrl = string.Empty;
    private string? _homepage;
    private string? _faviconUrl;

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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
