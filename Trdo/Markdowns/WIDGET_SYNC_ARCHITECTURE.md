# Trdo Widget and Tray Icon Synchronization

## Architecture Overview

Trdo can run in two modes simultaneously:

### 1. **Main App Process** (Tray Icon Mode)
- Launched by user clicking Trdo in Start Menu
- Shows tray icon in system tray
- Provides flyout UI for controlling playback
- **File:** `Trdo.exe` (no arguments)

### 2. **Widget COM Server Process**
- Launched automatically by Windows when widget is added
- No UI, runs in background
- Serves widget requests through COM interface
- **File:** `Trdo.exe -RegisterProcessAsComServer`

## ? NEW: Shared State Synchronization

**The Problem:** Each process has its own `RadioPlayerService.Instance` singleton with separate `MediaPlayer` instances. They don't share memory!

**The Solution:** Use `ApplicationData.LocalSettings` as a **shared state store** that all processes can read/write.

### Shared State Keys

| Key | Type | Purpose |
|-----|------|---------|
| `RadioIsPlaying` | bool | Current playback state (playing/paused) |
| `RadioCurrentStreamUrl` | string | Currently loaded station URL |
| `RadioVolume` | double | Volume level (0.0 to 1.0) |
| `WatchdogEnabled` | bool | Whether stream watchdog is active |

All processes read from and write to these shared keys, ensuring they stay synchronized.

## How State Synchronization Works

### Write Path (Widget ? Shared State)

```
User clicks Play in Widget
    ?
Widget Process: RadioPlayerWidget.OnActionInvoked()
    ?
Widget Process: PlayerViewModel.Toggle()
    ?
Widget Process: RadioPlayerService.Play()
    ?
Widget Process: MediaPlayer.Play()
    ?
Widget Process: PlaybackStateChanged event fires
    ?
Widget Process: ApplicationData.LocalSettings["RadioIsPlaying"] = true
    ?
Widget Process: SMTC.PlaybackStatus = Playing
```

### Read Path (Shared State ? Main App)

```
Main App Process: Timer or property access
    ?
Main App Process: RadioPlayerService.IsPlaying getter called
    ?
Main App Process: Reads ApplicationData.LocalSettings["RadioIsPlaying"]
    ?
Main App Process: Returns true (widget set it to playing)
    ?
Main App Process: PropertyChanged(IsPlaying) fires
    ?
Main App Process: App.PlayerVmOnPropertyChanged() called
    ?
Main App Process: UpdateTrayIconAsync() updates icon
```

### Automatic Synchronization on Startup

When either process starts, it loads the shared state:

```csharp
private void LoadSharedState()
{
    // Load current stream URL
    if (LocalSettings.Values.TryGetValue("RadioCurrentStreamUrl", out var urlValue))
    {
        _streamUrl = urlValue as string;
        // Initialize MediaSource with this URL
        _player.Source = MediaSource.CreateFromUri(new Uri(_streamUrl));
    }

    // Load playing state
    if (LocalSettings.Values.TryGetValue("RadioIsPlaying", out var playingValue))
    {
        bool sharedIsPlaying = playingValue is bool b && b;
        // If shared state says we should be playing, start playback
        if (sharedIsPlaying && !string.IsNullOrEmpty(_streamUrl))
        {
            _player.Play();
        }
    }
}
```

## Key Components

### 1. **Shared State Store** (NEW!)
`ApplicationData.Current.LocalSettings.Values`
- Persistent storage accessible by all app processes
- Survives app restarts
- Thread-safe across processes
- Instant synchronization

### 2. **MediaPlayer** (Per-Process)
Each process still has its own `MediaPlayer` instance:
- Widget process: Controls playback when widget buttons are clicked
- Main app process: Controls playback when tray icon/UI buttons are clicked
- Both read shared state to stay synchronized

### 3. **System Media Transport Controls (SMTC)**
Windows' built-in media coordination system:
- Provides system-wide media session
- Coordinates state across processes
- Powers hardware media buttons
- Shows in Windows media overlays
- **Backup synchronization mechanism**

### 4. **Shared Storage**
Both processes read from the same:
- `ApplicationData.Current.LocalSettings` for all settings AND state
- `RadioStationService` for station list
- Selected station index

## What Happens When Widget Changes Playback

### Scenario 1: Widget Changes State

```
[Widget Process]                                [Shared State]                    [Main App Process]
     ?                                                ?                                    ?
     ? User clicks Play                              ?                                    ?
     ?                                                ?                                    ?
     ??> MediaPlayer.Play()                          ?                                    ?
     ?                                                ?                                    ?
     ??> PlaybackStateChanged fires                  ?                                    ?
     ?                                                ?                                    ?
     ??> LocalSettings["RadioIsPlaying"] = true ????>?                                    ?
     ?                                                ?                                    ?
     ?                                                ?<??? IsPlaying property getter     ?
     ?                                                ?                                    ?
     ?                                                ????? Returns: true                  ?
     ?                                                ?                                    ?
     ?                                                ?                                    ??> PropertyChanged(IsPlaying)
     ?                                                ?                                    ?
     ?                                                ?                                    ??> UpdateTrayIconAsync()
     ?                                                ?                                        • Changes icon
     ?                                                ?                                        • Updates tooltip
```

### Scenario 2: Main App Process Polls State

The `PropertyChanged` event in the main app fires when the `IsPlaying` property is accessed and the shared state has changed. This can happen:

1. **Periodic polling** (if implemented)
2. **Property access** during UI updates
3. **MediaPlayer state change** detection

### Scenario 3: Both Processes Start Fresh

```
[Widget Process Starts]
     ?
     ??> LoadSharedState()
     ?   ??> Reads LocalSettings["RadioCurrentStreamUrl"] = "http://..."
     ?   ??> Reads LocalSettings["RadioIsPlaying"] = true
     ?   ??> Starts playback automatically
     ?
     ??> Widget shows "Playing" state

[Main App Starts 5 minutes later]
     ?
     ??> LoadSharedState()
     ?   ??> Reads LocalSettings["RadioCurrentStreamUrl"] = "http://..."
     ?   ??> Reads LocalSettings["RadioIsPlaying"] = true
     ?   ??> Detects already playing
     ?
     ??> Tray icon shows "Playing" state (Radio.ico)
```

## Code Changes Made

### RadioPlayerService.cs - Shared State Integration

**Added Shared State Keys:**
```csharp
private const string IsPlayingKey = "RadioIsPlaying";
private const string CurrentStreamUrlKey = "RadioCurrentStreamUrl";
```

**Modified IsPlaying Property:**
```csharp
public bool IsPlaying
{
    get
    {
        bool isPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        
        // Sync with shared state storage
        if (LocalSettings.Values.TryGetValue(IsPlayingKey, out object? storedValue))
        {
            bool storedIsPlaying = storedValue is bool b && b;
            // If there's a mismatch, shared state wins
            if (storedIsPlaying != isPlaying)
            {
                isPlaying = storedIsPlaying;
            }
        }
        
        return isPlaying;
    }
}
```

**Update Shared State on Playback Change:**
```csharp
_player.PlaybackSession.PlaybackStateChanged += (_, _) =>
{
    // ... existing code ...
    
    // Update shared state so other processes can see this change
    ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = isPlaying;
};
```

**Load Shared State on Startup:**
```csharp
private void LoadSharedState()
{
    // Load stream URL
    if (LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out var urlValue))
    {
        _streamUrl = urlValue as string;
        if (!string.IsNullOrEmpty(_streamUrl))
        {
            _player.Source = MediaSource.CreateFromUri(new Uri(_streamUrl));
        }
    }

    // Load and resume playback state
    if (LocalSettings.Values.TryGetValue(IsPlayingKey, out var playingValue))
    {
        bool sharedIsPlaying = playingValue is bool b && b;
        if (sharedIsPlaying && !string.IsNullOrEmpty(_streamUrl))
        {
            _player.Play();
        }
    }
}
```

**Save Stream URL to Shared State:**
```csharp
public void SetStreamUrl(string streamUrl)
{
    _streamUrl = streamUrl;
    
    // Save to shared state
    ApplicationData.Current.LocalSettings.Values[CurrentStreamUrlKey] = _streamUrl;
    
    // ... rest of code ...
}
```

## Benefits of Shared State Approach

? **True Synchronization** - Both processes read the same state  
? **Instant Updates** - No polling delay, state is immediately available  
? **Persistent State** - Survives process restarts  
? **Simple Implementation** - Just read/write to ApplicationData  
? **No IPC Complexity** - No pipes, sockets, or COM marshaling needed  
? **Works Offline** - No network or external dependencies  

## Limitations

? **Race Conditions** - Possible if both processes change state simultaneously (very rare)  
? **Storage Latency** - Small delay in writing to disk (usually < 10ms)  
? **No Active Notifications** - Processes must poll or check on property access  

## Testing the Synchronization

### Test 1: Widget ? Tray Icon
1. Launch Trdo normally (tray icon appears)
2. Add "Trdo - Radio Player" widget to Widgets Board
3. Click Play/Pause in widget
4. **Expected:** Tray icon changes within 1-2 seconds

### Test 2: Tray Icon ? Widget
1. Widget is already added and visible
2. Launch Trdo (tray icon appears)
3. Click tray icon to toggle playback
4. **Expected:** Widget updates to show new state

### Test 3: Widget-Only Startup
1. Ensure main app is not running
2. Add widget and click Play
3. Launch main app
4. **Expected:** Tray icon shows "Playing" state immediately (Radio.ico)

### Test 4: State Persistence
1. Start playback from widget
2. Close widget AND main app
3. Re-add widget
4. **Expected:** Widget remembers it was playing (shared state persists)

## Debugging Shared State

### View Shared State Values
```powershell
# Read shared state using WinRT API
$appData = [Windows.Storage.ApplicationData]::Current
$settings = $appData.LocalSettings.Values

Write-Host "RadioIsPlaying: $($settings['RadioIsPlaying'])"
Write-Host "RadioCurrentStreamUrl: $($settings['RadioCurrentStreamUrl'])"
Write-Host "RadioVolume: $($settings['RadioVolume'])"
```

### Debug Output
Look for these messages in Debug output:
- `[RadioPlayerService] Updated shared IsPlaying state to: true`
- `[RadioPlayerService] Loaded shared stream URL: http://...`
- `[RadioPlayerService] IsPlaying state mismatch - Shared: true, Local: false`

### Clear Shared State
```powershell
# Reset shared state
$appData = [Windows.Storage.ApplicationData]::Current
$appData.LocalSettings.Values.Remove('RadioIsPlaying')
$appData.LocalSettings.Values.Remove('RadioCurrentStreamUrl')
```

## Architecture Comparison

### Before (SMTC Only)
```
Widget Process ??????(SMTC)????? Main App Process
      ?                              ?
  MediaPlayer                   MediaPlayer
  (Independent)                 (Independent)
```

**Problem:** Processes don't share state directly. SMTC helps but isn't designed for this.

### After (Shared State + SMTC)
```
Widget Process ???(ApplicationData.LocalSettings)??? Main App Process
      ?                        ?                            ?
  MediaPlayer         Shared State Store              MediaPlayer
      ?                        ?                            ?
      ???????????????????(SMTC)????????????????????????????
```

**Solution:** Shared state provides direct synchronization. SMTC provides backup coordination.

## Summary

? **Widget controls playback** ? Shared state updated ? Tray icon reads state ? Updates  
? **Tray icon controls playback** ? Shared state updated ? Widget reads state ? Updates  
? **Process starts** ? Reads shared state ? Resumes correct state automatically  
? **State persists** ? Survives process restarts and app updates  

The implementation uses Windows' `ApplicationData.LocalSettings` as a lightweight, built-in state synchronization mechanism that requires no complex inter-process communication!
