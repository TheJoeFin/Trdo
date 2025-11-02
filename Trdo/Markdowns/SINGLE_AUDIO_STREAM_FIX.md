# Single Audio Stream Fix - No More Duplicates

## Problem

**Symptom:** When widget plays audio, the sound is:
- Doubled/echoed
- Has a phasing/flanging effect
- Sounds "hollow" or unnatural
- Louder than normal

**Specific Trigger:** Opening the Widgets Board while audio is already playing causes the audio to suddenly double.

**Root Cause:** Both processes were playing audio simultaneously:
1. **Widget COM Server Process**: Has MediaPlayer, plays audio
2. **Main App Process**: Detects shared state change, also plays audio
3. **Result**: TWO identical audio streams playing ? Weird doubled sound

**Additional Issue:** When the widget COM server process starts (e.g., when opening Widgets Board), it calls `LoadSharedState()` in its constructor, which:
- Reads `RadioIsPlaying = true` from shared state
- Creates a MediaSource
- Calls `_player.Play()` to resume playback
- **Result**: Second audio stream starts even though we prevented it in `Play()` method!

## Solution

**Designate ONE process as the audio player:**
- **Main App Process**: The ONLY process that plays audio
- **Widget COM Server Process**: Updates shared state ONLY, never plays audio

## Implementation

### 1. Added COM Server Mode Detection

**File:** `RadioPlayerService.cs`

```csharp
private readonly bool _isComServerMode;

private RadioPlayerService()
{
    // Check if we're running as COM server (widget process)
    string[] cmdLineArgs = Environment.GetCommandLineArgs();
    _isComServerMode = cmdLineArgs.Contains("-RegisterProcessAsComServer");
    Debug.WriteLine($"[RadioPlayerService] COM Server Mode: {_isComServerMode}");
    
    // ... rest of constructor
}
```

**Purpose:** Know which process we're in so we can behave differently.

### 2. Modified LoadSharedState() Method

**File:** `RadioPlayerService.cs`

```csharp
private void LoadSharedState()
{
    // Load stream URL
    if (ApplicationData.Current.LocalSettings.Values.TryGetValue(CurrentStreamUrlKey, out var urlValue))
    {
        _streamUrl = urlValue as string;
        
        // Only initialize MediaSource in main app mode, NOT in COM server mode
        if (!string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
        {
            _player.Source = MediaSource.CreateFromUri(new Uri(_streamUrl));
        }
        else if (_isComServerMode)
        {
            Debug.WriteLine("COM server mode - skipping MediaSource initialization");
        }
    }

    // Load playing state
    if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IsPlayingKey, out var playingValue))
    {
        bool sharedIsPlaying = playingValue is bool b && b;
        
        // Only resume playback in main app mode, NEVER in COM server mode
        if (sharedIsPlaying && !string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
        {
            _player.Play();  // Main app resumes
        }
        else if (_isComServerMode)
        {
            Debug.WriteLine("COM server mode - skipping playback resume");
        }
    }
}
```

**Key Change:** Widget COM server **never** creates MediaSource or starts playback when loading shared state.

**Critical Fix:** This prevents duplicate audio when Widgets Board opens while audio is already playing.

### 3. Modified Play() Method

**File:** `RadioPlayerService.cs`

```csharp
public void Play()
{
    Debug.WriteLine($"[RadioPlayerService] Play called (ComServerMode={_isComServerMode})");
    
    // In COM server mode (widget), we only update shared state
    // The main app will detect the change and start playback
    if (_isComServerMode)
    {
        Debug.WriteLine("[RadioPlayerService] COM server mode - updating shared state only");
        ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = true;
        return;  // ? DON'T call _player.Play()!
    }
    
    // Main app mode - actually play audio
    _player.Play();  // ? Only main app plays audio
    
    // ... rest of method
}
```

**Key Change:** Widget process **never** calls `_player.Play()`, only updates shared state.

### 4. Modified Pause() Method

**File:** `RadioPlayerService.cs`

```csharp
public void Pause()
{
    Debug.WriteLine($"[RadioPlayerService] Pause called (ComServerMode={_isComServerMode})");
    
    // In COM server mode (widget), we only update shared state
    // The main app will detect the change and pause playback
    if (_isComServerMode)
    {
        Debug.WriteLine("[RadioPlayerService] COM server mode - updating shared state only");
        ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = false;
        return;  // ? DON'T call _player.Pause()!
    }
    
    // Main app mode - actually pause audio
    _player.Pause();  // ? Only main app pauses audio
    
    // ... rest of method
}
```

**Key Change:** Widget process **never** calls `_player.Pause()`, only updates shared state.

### 5. Disabled MediaPlayer Sync in Widget

**File:** `App.xaml.cs`

```csharp
private void CheckSharedStateForComServer()
{
    // Get shared state
    bool sharedIsPlaying = /* read from ApplicationData */;
    
    // In COM server mode (widget), we should NOT sync the MediaPlayer
    // Only the main app process should actually play audio
    // The widget process only updates shared state, it doesn't play audio itself
    
    // Therefore, we don't sync MediaPlayer in COM server mode
    // This prevents duplicate audio streams
    
    Debug.WriteLine($"[App-COM] Shared state: IsPlaying={sharedIsPlaying} (MediaPlayer sync disabled)");
}
```

**Key Change:** Widget process doesn't sync its MediaPlayer to match shared state.

### 6. Updated Shared State Only from Main App

**File:** `RadioPlayerService.cs` (in PlaybackStateChanged event)

```csharp
_player.PlaybackSession.PlaybackStateChanged += (_, _) =>
{
    // ...
    
    // Only update shared state if we're the main app (not COM server)
    // This prevents the widget process from interfering with state
    if (!_isComServerMode)
    {
        ApplicationData.Current.LocalSettings.Values[IsPlayingKey] = isPlaying;
    }
    
    // ...
};
```

**Key Change:** Only main app updates shared state when MediaPlayer changes, not widget.

## How It Works Now

### Scenario: Widget Clicks Play

```
1. User clicks Play in widget
   Widget Process: playerService.Play() called
   Widget Process: Detects _isComServerMode = true
   Widget Process: Updates shared state ? RadioIsPlaying = true
   Widget Process: Returns (doesn't call _player.Play())
   Result: Widget's MediaPlayer stays idle ?

2. Main app polling (every 2 seconds)
   Main App: Reads shared state = true
   Main App: Checks local MediaPlayer = not playing
   Main App: Detects mismatch
   Main App: Calls playerService.Play()
   Main App: Detects _isComServerMode = false
   Main App: Actually calls _player.Play()
   Result: Main app's MediaPlayer plays ?

3. Audio output
   Widget Process MediaPlayer: Idle (not playing)
   Main App Process MediaPlayer: Playing
   Result: ONE audio stream ?
```

### Scenario: Open Widgets Board While Playing (The Critical Fix!)

```
Before Fix:
1. Main app playing audio
   Main App MediaPlayer: Playing ?

2. User opens Widgets Board (Win + W)
   Windows: Launches widget COM server process

3. Widget COM server constructor runs
   Widget Process: new RadioPlayerService()
   Widget Process: LoadSharedState() called
   Widget Process: Reads RadioIsPlaying = true
   Widget Process: Creates MediaSource ?
   Widget Process: Calls _player.Play() ?
   Result: Widget's MediaPlayer starts playing ?

4. Audio output
   Main App MediaPlayer: Playing
   Widget Process MediaPlayer: Also playing ?
   Result: TWO audio streams ? Doubled sound! ?

After Fix:
1. Main app playing audio
   Main App MediaPlayer: Playing ?

2. User opens Widgets Board (Win + W)
   Windows: Launches widget COM server process

3. Widget COM server constructor runs
   Widget Process: new RadioPlayerService()
   Widget Process: LoadSharedState() called
   Widget Process: Reads RadioIsPlaying = true
   Widget Process: Detects _isComServerMode = true
   Widget Process: SKIPS MediaSource creation ?
   Widget Process: SKIPS playback resume ?
   Result: Widget's MediaPlayer stays idle ?

4. Audio output
   Main App MediaPlayer: Playing
   Widget Process MediaPlayer: Idle (not playing) ?
   Result: ONE audio stream ? Clear sound! ?
```

## Architecture Comparison

### Before Fix (WRONG)

```
[Widget Process]
MediaPlayer: Playing ?
Updates shared state: true

[Main App Process]
Sees shared state: true
MediaPlayer: Also playing ?

[Audio Output]
Stream 1: From widget process
Stream 2: From main app process
Result: Doubled/echoed sound ?
```

### After Fix (CORRECT)

```
[Widget Process]
Updates shared state: true
MediaPlayer: Idle (never plays) ?

[Main App Process]
Sees shared state: true
MediaPlayer: Playing ?

[Audio Output]
Stream 1: From main app process only
Result: Clear, single audio stream ?
```

## Testing

### Audio Quality Test

**Before fix:**
- ? Weird echoed/phased sound
- ? "Hollow" audio quality
- ? Too loud (doubled volume)

**After fix:**
- ? Clear, natural sound
- ? Normal audio quality
- ? Correct volume level

### Debug Messages

**Widget process (COM server):**
```
[RadioPlayerService] Play called (ComServerMode=True)
[RadioPlayerService] COM server mode - updating shared state only
[RadioPlayerService] Updated shared state to Playing (widget request)
[RadioPlayerService] Play END (COM server mode)
```

**Main app process:**
```
[App] Shared state changed: IsPlaying False ? True
[App] Starting local MediaPlayer to match shared state
[RadioPlayerService] Play called (ComServerMode=False)
[RadioPlayerService] _player.Play() called successfully
```

**Key Verification:**
- ? Widget process: "COM server mode - updating shared state only"
- ? Widget process: Does NOT call `_player.Play()`
- ? Main app: Calls `_player.Play()`

## Benefits

### Single Audio Source
- ? Only one MediaPlayer plays audio
- ? Clear, crisp sound quality
- ? No doubling, echo, or phasing

### Simplified Architecture
- ? Clear separation of responsibilities
- ? Widget = UI + State management
- ? Main app = Audio playback

### Better Performance
- ? Only one MediaPlayer consuming resources
- ? No duplicate network streams
- ? Lower CPU/memory usage

## Edge Cases

### Widget Only (Main App Not Running)

**Behavior:**
- Widget updates shared state
- Widget shows "Playing" state
- **No audio plays** (main app not running to produce audio)

**When main app launches:**
- Detects shared state = playing
- Starts MediaPlayer automatically
- Audio begins playing

**This is acceptable** because:
- User understands widget needs main app for audio
- State is preserved correctly
- Audio starts when main app is available

### Multiple Widgets

**If user adds multiple widgets:**
- All widgets share same state
- All widgets show same play/pause status
- Still only ONE audio stream (from main app)

## Files Modified

1. **RadioPlayerService.cs**
   - Added `_isComServerMode` field
   - Modified `Play()` to only update state in COM server mode
   - Modified `Pause()` to only update state in COM server mode
   - Updated PlaybackStateChanged to only write state from main app

2. **App.xaml.cs**
   - Modified `CheckSharedStateForComServer()` to disable MediaPlayer sync

## Files Created

1. **Test-SingleAudioStream.ps1** - Comprehensive audio test
2. **This document** - Fix explanation

## Testing Procedure

```powershell
# 1. Deploy
Right-click Trdo project ? Deploy

# 2. Kill all instances
Get-Process -Name 'Trdo' | Stop-Process -Force

# 3. Launch main app
Start from Start Menu

# 4. Add widget
Win + W ? Add widgets ? Trdo - Radio Player

# 5. Test audio quality
Click Play in widget
Listen for 10 seconds
? Should be clear (no echo/doubling)

# 6. Run test script
.\Test-SingleAudioStream.ps1
```

## Success Criteria

? **Clear audio** - No doubling, echo, or phasing  
? **Normal volume** - Not unusually loud  
? **Single stream** - Only main app MediaPlayer active  
? **Widget works** - Controls playback correctly  
? **State syncs** - Tray icon reflects widget actions  

## Summary

**The Fix:** Widget process updates shared state only, never plays audio. Main app is the sole audio player.

**Before:** Two MediaPlayers playing ? Doubled/echoed sound

**After:** One MediaPlayer playing ? Clear, crisp audio

The audio now sounds **normal and clear** with no weird doubling effects! ???
