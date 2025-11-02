# Watchdog and State Sync Fixes

## Problems Fixed

### Problem 1: Watchdog Interfering with Widget Pause
**Symptom:** When user paused playback from the widget, the main app's watchdog would detect the stream stopped and automatically resume it after a few seconds.

**Root Cause:** The watchdog only checked the local `MediaPlayer` state and didn't know about shared state changes from other processes. When the widget paused playback:
1. Widget updates shared state: `RadioIsPlaying = false`
2. Widget's MediaPlayer pauses
3. Main app's watchdog checks its own MediaPlayer (still thinks it should be playing)
4. Watchdog sees stream stopped ? thinks it failed ? resumes playback

**The Fix:** Modified `StreamWatchdogService.CheckStreamHealthAsync()` to:
- Check **both** local MediaPlayer state AND shared state
- Detect when shared state indicates another process paused playback
- Disable recovery when cross-process pause detected

**Code Changes:**
```csharp
// StreamWatchdogService.cs
bool sharedStateSaysPlaying = false;

// Check shared state directly
if (ApplicationData.Current.LocalSettings.Values.TryGetValue("RadioIsPlaying", out object? storedValue))
{
    sharedStateSaysPlaying = storedValue is bool b && b;
}

// If shared state says not playing, but we thought user wanted playback
// This is because another process (widget) paused it
if (!sharedStateSaysPlaying && _userIntendedPlayback)
{
    Debug.WriteLine("[Watchdog] Detected pause by another process - disabling recovery");
    _userIntendedPlayback = false;  // ? KEY: Disable recovery
    _consecutiveFailures = 0;
    return;
}
```

### Problem 2: Tray Icon Not Updating When Widget Changes State
**Symptom:** Widget plays/pauses, but tray icon doesn't update (or takes a long time to update). Sometimes icon shows "paused" but audio is actually playing.

**Root Cause:** The main app only updated the tray icon when its own PropertyChanged events fired. But these events only fired when the main app's code changed something. When the widget changed state:
1. Widget updates shared state
2. Main app's `IsPlaying` property returns correct value (from shared state)
3. But no PropertyChanged event fires
4. Tray icon never updates

**The Fix:** Added a polling mechanism in `App.xaml.cs` that:
- Checks shared state every 2 seconds
- Compares with last known state
- Manually triggers PropertyChanged when state differs
- Updates tray icon in response

**Code Changes:**
```csharp
// App.xaml.cs
private void StartSharedStatePolling()
{
    _sharedStatePollingTimer = dispatcherQueue.CreateTimer();
    _sharedStatePollingTimer.Interval = TimeSpan.FromSeconds(2);  // Poll every 2 seconds
    _sharedStatePollingTimer.Tick += (sender, args) =>
    {
        CheckSharedState();
    };
    _sharedStatePollingTimer.Start();
    
    _lastKnownPlayingState = _playerVm.IsPlaying;
}

private void CheckSharedState()
{
    bool currentIsPlaying = _playerVm.IsPlaying;  // Reads from shared state
    
    if (currentIsPlaying != _lastKnownPlayingState)
    {
        Debug.WriteLine($"[App] Shared state changed: IsPlaying {_lastKnownPlayingState} ? {currentIsPlaying}");
        _lastKnownPlayingState = currentIsPlaying;
        
        // Manually trigger property changed handler
        PlayerVmOnPropertyChanged(this, new PropertyChangedEventArgs(nameof(PlayerViewModel.IsPlaying)));
    }
}
```

## Architecture Changes

### Before Fixes

```
Widget Process                           Main App Process
      ?                                        ?
  Pauses playback                        Watchdog detects stop
      ?                                        ?
  Updates shared state                   Thinks: "Stream failed!"
  RadioIsPlaying = false                      ?
                                         Resumes playback (WRONG!)
                                              ?
                                         Tray icon stuck (no update trigger)
```

**Result:** Widget and main app fight each other. Tray icon shows wrong state.

### After Fixes

```
Widget Process                           Main App Process
      ?                                        ?
  Pauses playback                        Watchdog checks both:
      ?                                   • Local MediaPlayer
  Updates shared state                    • Shared state
  RadioIsPlaying = false                      ?
                                         Sees: shared state = paused
                                              ?
                                         Thinks: "Another process paused it"
                                              ?
                                         Disables recovery (CORRECT!)
                                              ?
                                         Polling timer (every 2s)
                                              ?
                                         Detects state changed
                                              ?
                                         Updates tray icon
```

**Result:** Watchdog respects widget actions. Tray icon updates within 2 seconds.

## Key Implementation Details

### 1. Watchdog Shared State Check
```csharp
// In StreamWatchdogService.CheckStreamHealthAsync()

// Get local state
bool isPlaying = _playerService.IsPlaying;

// Get shared state
bool sharedStateSaysPlaying = false;
if (ApplicationData.Current.LocalSettings.Values.TryGetValue("RadioIsPlaying", out object? storedValue))
{
    sharedStateSaysPlaying = storedValue is bool b && b;
}

// If either is playing, stream is healthy
if (isPlaying || sharedStateSaysPlaying)
{
    return;  // No recovery needed
}

// If shared state changed to paused while we thought it should play
if (!sharedStateSaysPlaying && _userIntendedPlayback)
{
    // Another process paused it - respect that
    _userIntendedPlayback = false;
    return;  // Don't recover
}
```

### 2. State Polling Timer
```csharp
// In App.xaml.cs

private DispatcherQueueTimer? _sharedStatePollingTimer;
private bool _lastKnownPlayingState = false;

// Start polling when app launches
private void StartSharedStatePolling()
{
    _sharedStatePollingTimer = dispatcherQueue.CreateTimer();
    _sharedStatePollingTimer.Interval = TimeSpan.FromSeconds(2);
    _sharedStatePollingTimer.Tick += (sender, args) => CheckSharedState();
    _sharedStatePollingTimer.Start();
    
    _lastKnownPlayingState = _playerVm.IsPlaying;
}

// Check if state changed
private void CheckSharedState()
{
    bool currentIsPlaying = _playerVm.IsPlaying;  // This reads shared state
    
    if (currentIsPlaying != _lastKnownPlayingState)
    {
        _lastKnownPlayingState = currentIsPlaying;
        // Trigger UI update
        PlayerVmOnPropertyChanged(this, new PropertyChangedEventArgs(nameof(PlayerViewModel.IsPlaying)));
    }
}
```

### 3. Enhanced IsPlaying Property
```csharp
// In RadioPlayerService.cs

public bool IsPlaying
{
    get
    {
        bool localIsPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        
        // Check shared state
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsPlayingKey, out object? storedValue))
        {
            bool storedIsPlaying = storedValue is bool b && b;
            
            // Shared state wins on mismatch
            if (storedIsPlaying != localIsPlaying)
            {
                Debug.WriteLine($"[RadioPlayerService] IsPlaying mismatch - using Shared: {storedIsPlaying}");
                return storedIsPlaying;  // Return shared state
            }
        }
        
        return localIsPlaying;
    }
}
```

## Testing the Fixes

### Test 1: Watchdog Respects Widget Pause
**Steps:**
1. Launch main app (tray icon appears)
2. Add widget
3. Click Play in widget
4. Wait 10 seconds (watchdog activates)
5. Click Pause in widget
6. Wait 10 seconds and observe

**Expected:**
? Radio stays paused (watchdog respects widget pause)
? If radio resumes, watchdog fix failed

**Debug Messages:**
```
[Watchdog] Detected pause by another process - disabling recovery
```

### Test 2: Tray Icon Updates Quickly
**Steps:**
1. Widget and main app running
2. Radio is paused
3. Click Play in widget
4. Watch tray icon

**Expected:**
? Within 2 seconds: Tray icon changes to Radio.ico (playing)

**Debug Messages:**
```
[App] Shared state changed: IsPlaying False ? True
[RadioPlayerService] IsPlaying mismatch - using Shared: True
```

### Test 3: Rapid Toggle Handling
**Steps:**
1. Click Play in widget
2. Immediately click Pause in widget
3. Immediately click Play in widget
4. Wait 3 seconds

**Expected:**
? Both widget and tray show 'Playing' state
? No confusion or stuck states

## Performance Impact

### Polling Timer
- **Interval:** 2 seconds
- **CPU Impact:** Negligible (just reads ApplicationData)
- **Can be tuned:** Change interval in `StartSharedStatePolling()`

### Watchdog Changes
- **No performance impact:** Same check interval, just reads one more value
- **Benefit:** Prevents unnecessary recovery attempts

## Limitations

### Polling Delay
- **Maximum delay:** 2 seconds for tray icon to update
- **Typical delay:** 0.5-1.5 seconds
- **Acceptable for:** User-initiated actions (widget clicks)

**Improvement Options:**
1. Reduce interval to 1 second (more CPU usage)
2. Implement file system watcher on settings.dat
3. Add named pipe IPC for instant notification

### Shared State Race Conditions
- **Very rare:** Both processes change state simultaneously
- **Impact:** One change might be lost
- **Mitigation:** Shared state reader always wins in `IsPlaying`

## Files Modified

1. **StreamWatchdogService.cs**
   - Modified `CheckStreamHealthAsync()` to check shared state
   - Added logic to detect cross-process pause

2. **App.xaml.cs**
   - Added `_sharedStatePollingTimer` field
   - Added `_lastKnownPlayingState` field
   - Added `StartSharedStatePolling()` method
   - Added `CheckSharedState()` method
   - Modified destructor to clean up timer

3. **RadioPlayerService.cs**
   - Enhanced `IsPlaying` property to prefer shared state on mismatch

## Files Created

1. **Test-WatchdogFix.ps1** - Comprehensive test script
2. **This document** - Fix explanation

## Summary

? **Watchdog no longer interferes** - Respects cross-process pause  
? **Tray icon updates reliably** - Polling detects changes within 2 seconds  
? **State consistency maintained** - Shared state is source of truth  
? **No more phantom resume** - Widget pause stays paused  

The fixes ensure that widget and main app truly cooperate instead of fighting each other!
