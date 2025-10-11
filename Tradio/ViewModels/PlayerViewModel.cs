using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tradio.Services;

namespace Tradio.ViewModels;

public sealed partial class PlayerViewModel : INotifyPropertyChanged
{
    private readonly RadioPlayerService _player = RadioPlayerService.Instance;
    private const string DefaultStreamUrl = "https://wyms.streamguys1.com/live?platform=NPR&uuid=xhjlsf05e";
    private string _streamUrl;
    private string _watchdogStatus = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel()
    {
        _streamUrl = DefaultStreamUrl;
        _player.Initialize(_streamUrl);
        _player.PlaybackStateChanged += (_, _) => OnPropertyChanged(nameof(IsPlaying));
        _player.VolumeChanged += (_, _) => OnPropertyChanged(nameof(Volume));
        
        // Subscribe to watchdog status changes
        _player.Watchdog.StreamStatusChanged += (_, args) =>
        {
            WatchdogStatus = $"{args.Status}: {args.Message}";
        };
    }

    public bool IsPlaying => _player.IsPlaying;

    public string StreamUrl
    {
        get => _streamUrl;
        set
        {
            if (value == _streamUrl) return;
            _streamUrl = value;
            OnPropertyChanged();
        }
    }

    public bool WatchdogEnabled
    {
        get => _player.WatchdogEnabled;
        set
        {
            if (value == _player.WatchdogEnabled) return;
            _player.WatchdogEnabled = value;
            OnPropertyChanged();
        }
    }

    public string WatchdogStatus
    {
        get => _watchdogStatus;
        private set
        {
            if (value == _watchdogStatus) return;
            _watchdogStatus = value;
            OnPropertyChanged();
        }
    }

    public bool ApplyStreamUrl()
    {
        if (!IsValidUrl(_streamUrl))
            return false;
        _player.SetStreamUrl(_streamUrl);
        return true;
    }

    private static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

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