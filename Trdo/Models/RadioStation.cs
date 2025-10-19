using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trdo.Models;

public class RadioStation : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _streamUrl = string.Empty;

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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
