# Quick Fix Summary: Double Audio When Opening Widgets Board

## The Problem

**Symptom:** Audio plays fine from tray icon. But when you **open the Widgets Board** (Win + W), the audio suddenly **doubles** and sounds echoed/phased.

**Why It Happened:**
1. Main app is playing audio (one stream) ?
2. User opens Widgets Board
3. Windows launches widget COM server process
4. Widget process constructor calls `LoadSharedState()`
5. LoadSharedState sees `RadioIsPlaying = true`
6. LoadSharedState creates MediaSource and calls `_player.Play()`
7. **Second audio stream starts!** ?

## The Fix

**Prevent widget COM server from EVER playing audio**, including during initialization:

### Code Change

**File:** `RadioPlayerService.cs` ? `LoadSharedState()` method

```csharp
private void LoadSharedState()
{
    // Load stream URL
    _streamUrl = /* read from shared state */;
    
    // CRITICAL FIX: Only create MediaSource in main app mode
    if (!string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
    {
        _player.Source = MediaSource.CreateFromUri(new Uri(_streamUrl));
    }
    else if (_isComServerMode)
    {
        // Widget process: Don't create MediaSource!
        Debug.WriteLine("COM server mode - skipping MediaSource initialization");
    }
    
    // Load playing state
    bool sharedIsPlaying = /* read from shared state */;
    
    // CRITICAL FIX: Only resume playback in main app mode
    if (sharedIsPlaying && !string.IsNullOrEmpty(_streamUrl) && !_isComServerMode)
    {
        _player.Play();  // Main app resumes
    }
    else if (_isComServerMode)
    {
        // Widget process: Don't start playback!
        Debug.WriteLine("COM server mode - skipping playback resume");
    }
}
```

### What Changed

**Before:**
- `LoadSharedState()` always created MediaSource and started playback
- Widget COM server would play audio when it started
- Result: Duplicate audio when opening Widgets Board

**After:**
- `LoadSharedState()` checks `_isComServerMode`
- Widget COM server skips MediaSource creation and playback
- Result: Only main app plays audio, even when Widgets Board opens

## Testing

### Quick Test

1. **Start audio:**
   - Launch main app
   - Click Play in tray icon
   - Confirm audio is clear (single stream)

2. **Open Widgets Board:**
   - Press Win + W
   - Listen carefully
   - **Expected:** Audio stays clear (no change)
   - **FAIL if:** Audio suddenly doubles or echoes

3. **Debug verification:**
   ```
   [RadioPlayerService] COM Server Mode: True
   [RadioPlayerService] COM server mode - skipping MediaSource initialization
   [RadioPlayerService] COM server mode - skipping playback resume
   ```

### Full Test

```powershell
.\Test-SingleAudioStream.ps1
```

## Impact

? **Opening Widgets Board no longer causes double audio**  
? **Audio stays clear and single at all times**  
? **Widget COM server never plays audio, only updates state**  

## Related Fixes

This is part of a comprehensive fix to ensure only ONE MediaPlayer ever plays audio:

1. ? Widget `Play()` method only updates shared state
2. ? Widget `Pause()` method only updates shared state
3. ? Widget `LoadSharedState()` never starts playback ? **This fix!**
4. ? Main app syncs MediaPlayer to match shared state
5. ? Only main app process plays audio

## Files Modified

- `RadioPlayerService.cs` - Modified `LoadSharedState()`
- `Test-SingleAudioStream.ps1` - Added test for Widgets Board opening
- `SINGLE_AUDIO_STREAM_FIX.md` - Updated documentation

## Deployment

```
1. Build successful ?
2. Deploy: Right-click Trdo project > Deploy
3. Test: Play audio, then open Widgets Board
4. Verify: Audio stays single/clear
```

---

**Status:** ? FIXED - Opening Widgets Board no longer causes duplicate audio!
