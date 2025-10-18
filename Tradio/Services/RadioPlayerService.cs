using Microsoft.UI.Dispatching;
using System;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Tradio.Services;

public sealed partial class RadioPlayerService : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly DispatcherQueue _uiQueue; // capture UI dispatcher
    private readonly StreamWatchdogService _watchdog;
    private double _volume = 0.5; // default
    private const string VolumeKey = "RadioVolume";
    private const string WatchdogEnabledKey = "WatchdogEnabled";
    private bool _isInitialized;
    private string? _streamUrl;

    public static RadioPlayerService Instance { get; } = new();

    public event EventHandler<bool>? PlaybackStateChanged; // bool = isPlaying
    public event EventHandler<double>? VolumeChanged;

    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public string? StreamUrl => _streamUrl;

    public StreamWatchdogService Watchdog => _watchdog;

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

    public bool WatchdogEnabled
    {
        get => _watchdog.IsEnabled;
        set
        {
            _watchdog.IsEnabled = value;
            try
            {
                ApplicationData.Current.LocalSettings.Values[WatchdogEnabledKey] = value;
            }
            catch { }
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

        // Initialize watchdog service
        _watchdog = new StreamWatchdogService(this);

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(VolumeKey, out object? v))
            {
                double parsed = v switch
                {
                    double d => d,
                    string s when double.TryParse(s, out double d2) => d2,
                    _ => _volume
                };
                _volume = Math.Clamp(parsed, 0, 1);
                _player.Volume = _volume;
            }

            // Load watchdog enabled state (default to true for auto-recovery)
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(WatchdogEnabledKey, out object? w))
            {
                bool watchdogEnabled = w switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out bool b2) => b2,
                    _ => true
                };
                _watchdog.IsEnabled = watchdogEnabled;
            }
            else
            {
                // Enable watchdog by default
                _watchdog.IsEnabled = true;
            }
        }
        catch { }
    }

    public void Initialize(string streamUrl)
    {
        if (_isInitialized) return;
        SetStreamUrl(streamUrl);
    }

    public void SetStreamUrl(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl)) return;
        try
        {
            Uri uri = new(streamUrl);
            bool wasPlaying = false;
            try { wasPlaying = IsPlaying; } catch { }
            _player.AudioCategory = MediaPlayerAudioCategory.Media;
            _player.RealTimePlayback = true; // optimize for live streams
            _player.Source = MediaSource.CreateFromUri(uri);
            _isInitialized = true;
            _streamUrl = streamUrl;

            if (wasPlaying)
            {
                try { _player.Play(); } catch { }
            }
        }
        catch (Exception)
        {
            // Ignore invalid URLs here; caller should validate.
        }
    }

    public void Play()
    {
        if (!_isInitialized
            || string.IsNullOrWhiteSpace(_streamUrl)) return;

        if (_player.Source is MediaSource media && media.State == MediaSourceState.Opening)
            return; // already trying to open

        try
        {
            Uri uri = new(_streamUrl);
            _player.Source = MediaSource.CreateFromUri(uri);
            _player.Play();
            // Notify watchdog that user intentionally started playback
            _watchdog.NotifyUserIntentionToPlay();
        }
        catch (Exception) { }
    }

    public void Pause()
    {
        if (!_isInitialized) return;
        try
        {
            // For live streams, stop completely instead of pause
            // This ensures when resumed, we get the live stream, not buffered content
            _player.Pause();
            if (_player.Source is MediaSource media)
            {
                media?.Reset();
                media?.Dispose();
            }
            _player.Source = null;

            // Notify watchdog that user intentionally paused
            _watchdog.NotifyUserIntentionToPause();

            // Reinitialize the stream so it's ready to play again
            if (!string.IsNullOrEmpty(_streamUrl))
            {
                SetStreamUrl(_streamUrl);
            }
        }
        catch (Exception) { }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
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
        _watchdog.Dispose();
        _player.Dispose();
    }
}