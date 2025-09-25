using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tradio.Services;

namespace Tradio.ViewModels;

public sealed class PlayerViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private const string StreamUrl = "https://wyms.streamguys1.com/live?platform=NPR&uuid=xhjlsf05e";

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel()
    {
        _player.Initialize(StreamUrl);
        _player.PlaybackStateChanged += (_, _) => OnPropertyChanged(nameof(IsPlaying));
        _player.VolumeChanged += (_, _) => OnPropertyChanged(nameof(Volume));
    }

    public bool IsPlaying => _player.IsPlaying;

    public double Volume
    {
        get => _player.Volume;
        set
        {
            _player.Volume = value;
            OnPropertyChanged();
        }
    }

    public void Toggle() => _player.TogglePlayPause();

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}