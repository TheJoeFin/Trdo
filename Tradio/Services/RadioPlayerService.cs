using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Tradio.Services;

public sealed class RadioPlayerService : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly DispatcherQueue _uiQueue; // capture UI dispatcher
    private double _volume = 0.5; // default
    private const string VolumeKey = "RadioVolume";
    private bool _isInitialized;

    public static RadioPlayerService Instance { get; } = new();

    public event EventHandler<bool>? PlaybackStateChanged; // bool = isPlaying
    public event EventHandler<double>? VolumeChanged;

    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(_volume - value) < 0.0001) return;
            _volume = value;
            _player.Volume = _volume;
            try
            {
                ApplicationData.Current.LocalSettings.Values[VolumeKey] = _volume;
            }
            catch { }
            VolumeChanged?.Invoke(this, _volume);
        }
    }

    private RadioPlayerService()
    {
        // This constructor is invoked on the UI thread (first access happens in MainWindow).
        _uiQueue = DispatcherQueue.GetForCurrentThread();

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = false,
            IsLoopingEnabled = false,
            Volume = _volume
        };

        _player.PlaybackSession.PlaybackStateChanged += (_, _) =>
        {
            bool isPlaying;
            try
            {
                isPlaying = IsPlaying;
            }
            catch (Exception)
            {
                return; // bail out if player state not accessible
            }
            TryEnqueueOnUi(() => PlaybackStateChanged?.Invoke(this, isPlaying));
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(VolumeKey, out var v))
            {
                double parsed = v switch
                {
                    double d => d,
                    string s when double.TryParse(s, out var d2) => d2,
                    _ => _volume
                };
                _volume = Math.Clamp(parsed, 0, 1);
                _player.Volume = _volume;
            }
        }
        catch { }
    }

    public void Initialize(string streamUrl)
    {
        if (_isInitialized) return;
        _player.Source = MediaSource.CreateFromUri(new Uri(streamUrl));
        _isInitialized = true;
    }

    public void Play()
    {
        if (!_isInitialized) return;
        try { _player.Play(); } catch (Exception) { }
    }

    public void Pause()
    {
        if (!_isInitialized) return;
        try { _player.Pause(); } catch (Exception) { }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause(); else Play();
    }

    private void TryEnqueueOnUi(DispatcherQueueHandler action)
    {
        if (_uiQueue is null)
        {
            action();
            return;
        }
        if (_uiQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _uiQueue.TryEnqueue(action);
        }
    }

    public void Dispose()
    {
        _player.Dispose();
    }
}