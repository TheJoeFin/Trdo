# Test Single Audio Stream Fix
# Verifies that only ONE MediaPlayer plays audio at a time (no duplicates)

Write-Host "`n????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  Trdo Single Audio Stream Fix Verification              ?" -ForegroundColor Cyan
Write-Host "????????????????????????????????????????????????????????????`n" -ForegroundColor Cyan

Write-Host "This test verifies the single audio stream fix:" -ForegroundColor Yellow
Write-Host "  BEFORE: Widget plays ? Both processes play ? Doubled/echoed audio" -ForegroundColor Red
Write-Host "  AFTER:  Widget plays ? Only main app plays ? Clear, single audio" -ForegroundColor Green
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[ARCHITECTURE CHANGE]`n" -ForegroundColor Yellow

Write-Host "Old Architecture (WRONG - Duplicate Audio):" -ForegroundColor Red
Write-Host "  Widget Process:" -ForegroundColor White
Write-Host "    • Has MediaPlayer instance" -ForegroundColor Gray
Write-Host "    • Plays audio when widget clicks Play" -ForegroundColor Gray
Write-Host "    • Updates shared state" -ForegroundColor Gray
Write-Host "  Main App Process:" -ForegroundColor White
Write-Host "    • Has MediaPlayer instance" -ForegroundColor Gray
Write-Host "    • Sees shared state changed" -ForegroundColor Gray
Write-Host "    • Also plays audio" -ForegroundColor Gray
Write-Host "  Result: ???? TWO AUDIO STREAMS! Doubled/echoed sound ?" -ForegroundColor Red
Write-Host ""

Write-Host "New Architecture (CORRECT - Single Audio):" -ForegroundColor Green
Write-Host "  Widget Process:" -ForegroundColor White
Write-Host "    • Has MediaPlayer instance (but doesn't use it)" -ForegroundColor Gray
Write-Host "    • Only updates shared state when widget clicks Play/Pause" -ForegroundColor Gray
Write-Host "    • Does NOT play audio itself" -ForegroundColor Gray
Write-Host "  Main App Process:" -ForegroundColor White
Write-Host "    • Has MediaPlayer instance" -ForegroundColor Gray
Write-Host "    • Sees shared state changed" -ForegroundColor Gray
Write-Host "    • Plays/pauses audio based on shared state" -ForegroundColor Gray
Write-Host "  Result: ?? ONE AUDIO STREAM! Clear sound ?" -ForegroundColor Green
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[CRITICAL TEST] Single Audio Stream Check`n" -ForegroundColor Yellow

Write-Host "Test 1: Widget Play (Main App Running)" -ForegroundColor Cyan
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Launch main app (tray icon)" -ForegroundColor Gray
Write-Host "    2. Add widget to Widgets Board" -ForegroundColor Gray
Write-Host "    3. Click Play in widget" -ForegroundColor Gray
Write-Host "    4. Listen carefully to the audio" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected Behavior:" -ForegroundColor White
Write-Host "    ? Audio plays ONCE (clear, single stream)" -ForegroundColor Green
Write-Host "    ? No echo, doubling, or phasing effects" -ForegroundColor Green
Write-Host "    ? Volume sounds normal (not doubled)" -ForegroundColor Green
Write-Host ""
Write-Host "  FAIL Indicators:" -ForegroundColor Red
Write-Host "    ? Weird echoed/doubled sound" -ForegroundColor White
Write-Host "    ? Phasing or 'hollow' audio quality" -ForegroundColor White
Write-Host "    ? Volume seems too loud (two streams)" -ForegroundColor White
Write-Host ""

Write-Host "Test 2: Open Widgets Board While Playing (CRITICAL FIX!)" -ForegroundColor Cyan
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Launch main app" -ForegroundColor Gray
Write-Host "    2. Click Play in tray icon (audio starts)" -ForegroundColor Gray
Write-Host "    3. Close Widgets Board if open" -ForegroundColor Gray
Write-Host "    4. Listen - audio should be clear (single stream)" -ForegroundColor Gray
Write-Host "    5. Press Win + W to open Widgets Board" -ForegroundColor Gray
Write-Host "    6. Listen carefully immediately after board opens" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected Behavior:" -ForegroundColor White
Write-Host "    ? Audio stays SINGLE stream (no change in quality)" -ForegroundColor Green
Write-Host "    ? No sudden doubling when board opens" -ForegroundColor Green
Write-Host "    ? Widget COM server starts but doesn't play audio" -ForegroundColor Green
Write-Host ""
Write-Host "  FAIL Indicators:" -ForegroundColor Red
Write-Host "    ? Audio suddenly doubles when Widgets Board opens" -ForegroundColor White
Write-Host "    ? Echo appears when COM server process starts" -ForegroundColor White
Write-Host "    ? Audio quality changes (gets 'hollow')" -ForegroundColor White
Write-Host ""
Write-Host "  Debug Messages to Look For:" -ForegroundColor White
Write-Host "    Widget COM server starts:" -ForegroundColor Gray
Write-Host "    [RadioPlayerService] COM Server Mode: True" -ForegroundColor Cyan
Write-Host "    [RadioPlayerService] Loaded shared IsPlaying state: True" -ForegroundColor Cyan
Write-Host "    [RadioPlayerService] COM server mode - skipping playback resume" -ForegroundColor Cyan
Write-Host "    [RadioPlayerService] LoadSharedState END" -ForegroundColor Cyan
Write-Host ""

Write-Host "Test 3: Widget Play (Main App Not Running)" -ForegroundColor Cyan
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Ensure main app is NOT running" -ForegroundColor Gray
Write-Host "       (Check: Get-Process -Name 'Trdo' | Where-Object {$_.CommandLine -notlike '*RegisterProcessAsComServer*'})" -ForegroundColor Gray
Write-Host "    2. Add widget to Widgets Board" -ForegroundColor Gray
Write-Host "    3. Click Play in widget" -ForegroundColor Gray
Write-Host "    4. Wait 5 seconds" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected Behavior:" -ForegroundColor White
Write-Host "    ? Widget updates to 'Pause' button" -ForegroundColor Green
Write-Host "    ? Shared state updated (RadioIsPlaying = true)" -ForegroundColor Green
Write-Host "    ? NO audio plays (main app not running)" -ForegroundColor Yellow
Write-Host "  Then:" -ForegroundColor White
Write-Host "    5. Launch main app (from Start Menu)" -ForegroundColor Gray
Write-Host "    ? Main app detects shared state = playing" -ForegroundColor Green
Write-Host "    ? Main app starts playback" -ForegroundColor Green
Write-Host "    ? Audio starts playing (within 2 seconds)" -ForegroundColor Green
Write-Host ""

Write-Host "Test 4: Tray Play ? Widget Pause" -ForegroundColor Cyan
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Click Play in tray icon" -ForegroundColor Gray
Write-Host "    2. Listen - audio should be clear (single stream)" -ForegroundColor Gray
Write-Host "    3. Click Pause in widget" -ForegroundColor Gray
Write-Host "    4. Listen - audio should stop completely" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected:" -ForegroundColor White
Write-Host "    ? Audio is clear when playing" -ForegroundColor Green
Write-Host "    ? Audio stops completely when paused" -ForegroundColor Green
Write-Host "    ? No lingering sounds or echoes" -ForegroundColor Green
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[DEBUG MESSAGES TO LOOK FOR]`n" -ForegroundColor Yellow

Write-Host "When widget clicks Play:" -ForegroundColor White
Write-Host ""
Write-Host "Widget Process (COM Server):" -ForegroundColor Cyan
Write-Host "  [RadioPlayerService] Play called (ComServerMode=True)" -ForegroundColor Gray
Write-Host "  [RadioPlayerService] COM server mode - updating shared state only" -ForegroundColor Gray
Write-Host "  [RadioPlayerService] Updated shared state to Playing (widget request)" -ForegroundColor Gray
Write-Host "  [RadioPlayerService] Play END (COM server mode)" -ForegroundColor Gray
Write-Host ""

Write-Host "Main App Process:" -ForegroundColor Cyan
Write-Host "  [App] Shared state changed: IsPlaying False ? True" -ForegroundColor Gray
Write-Host "  [App] Syncing MediaPlayer: shared=True, localMediaPlayer=False" -ForegroundColor Gray
Write-Host "  [App] Starting local MediaPlayer to match shared state" -ForegroundColor Gray
Write-Host "  [RadioPlayerService] Play called (ComServerMode=False)" -ForegroundColor Gray
Write-Host "  [RadioPlayerService] _player.Play() called successfully" -ForegroundColor Gray
Write-Host ""

Write-Host "Key Indicators:" -ForegroundColor White
Write-Host "  ? Widget process: 'COM server mode - updating shared state only'" -ForegroundColor Green
Write-Host "  ? Widget process: Does NOT call _player.Play()" -ForegroundColor Green
Write-Host "  ? Main app: 'Starting local MediaPlayer to match shared state'" -ForegroundColor Green
Write-Host "  ? Main app: Calls _player.Play()" -ForegroundColor Green
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[CODE CHANGES SUMMARY]`n" -ForegroundColor Yellow

Write-Host "1. RadioPlayerService.cs:" -ForegroundColor White
Write-Host "   • Added _isComServerMode field" -ForegroundColor Gray
Write-Host "   • Detects COM server mode from command line args" -ForegroundColor Gray
Write-Host "   • Play() method:" -ForegroundColor Gray
Write-Host "     - COM server mode: Only updates shared state, returns early" -ForegroundColor Gray
Write-Host "     - Main app mode: Actually calls _player.Play()" -ForegroundColor Gray
Write-Host "   • Pause() method:" -ForegroundColor Gray
Write-Host "     - COM server mode: Only updates shared state, returns early" -ForegroundColor Gray
Write-Host "     - Main app mode: Actually calls _player.Pause()" -ForegroundColor Gray
Write-Host ""

Write-Host "2. App.xaml.cs:" -ForegroundColor White
Write-Host "   • CheckSharedStateForComServer():" -ForegroundColor Gray
Write-Host "     - Disabled MediaPlayer syncing in COM server mode" -ForegroundColor Gray
Write-Host "     - Only logs shared state, doesn't sync MediaPlayer" -ForegroundColor Gray
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[AUDIO QUALITY CHECK]`n" -ForegroundColor Yellow

Write-Host "Listen for these audio quality issues:" -ForegroundColor White
Write-Host ""
Write-Host "Bad (Two Streams):" -ForegroundColor Red
Write-Host "  • Phasing/flanging effect (sounds 'hollow')" -ForegroundColor White
Write-Host "  • Echo or slight delay between streams" -ForegroundColor White
Write-Host "  • Unusually loud volume" -ForegroundColor White
Write-Host "  • Distortion or clipping" -ForegroundColor White
Write-Host "  • Unnatural 'doubling' of sounds" -ForegroundColor White
Write-Host ""

Write-Host "Good (Single Stream):" -ForegroundColor Green
Write-Host "  • Clear, natural sound" -ForegroundColor White
Write-Host "  • Normal volume level" -ForegroundColor White
Write-Host "  • No echo or phasing" -ForegroundColor White
Write-Host "  • Crisp audio quality" -ForegroundColor White
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[VERIFICATION PROCEDURE]`n" -ForegroundColor Yellow

Write-Host "Step 1: Deploy Updated Build" -ForegroundColor Cyan
Write-Host "  • Build > Clean Solution" -ForegroundColor White
Write-Host "  • Build > Rebuild Solution" -ForegroundColor White
Write-Host "  • Right-click Trdo project > Deploy" -ForegroundColor White
Write-Host ""

Write-Host "Step 2: Close All Trdo Processes" -ForegroundColor Cyan
Write-Host "  Get-Process -Name 'Trdo' | Stop-Process -Force" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 3: Launch Main App" -ForegroundColor Cyan
Write-Host "  • Start Trdo from Start Menu" -ForegroundColor White
Write-Host "  • Verify tray icon appears" -ForegroundColor White
Write-Host ""

Write-Host "Step 4: Add Widget" -ForegroundColor Cyan
Write-Host "  • Press Win + W" -ForegroundColor White
Write-Host "  • Add 'Trdo - Radio Player' widget" -ForegroundColor White
Write-Host ""

Write-Host "Step 5: Test Audio Quality" -ForegroundColor Cyan
Write-Host "  • Click Play in widget" -ForegroundColor White
Write-Host "  • Listen carefully for 10 seconds" -ForegroundColor White
Write-Host "  • Check for echo, doubling, or phasing" -ForegroundColor White
Write-Host "  • Audio should be CLEAR and SINGLE" -ForegroundColor White
Write-Host ""

Write-Host "Step 6: Verify Process Isolation" -ForegroundColor Cyan
Write-Host "  • Check running processes:" -ForegroundColor White
Write-Host "    Get-Process -Name 'Trdo' | Format-Table Id, ProcessName" -ForegroundColor Cyan
Write-Host "  • Should see TWO processes (main app + widget)" -ForegroundColor White
Write-Host "  • Only main app should be playing audio" -ForegroundColor White
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "Expected Results:" -ForegroundColor Green
Write-Host "  ? Audio plays clearly from ONE source only" -ForegroundColor White
Write-Host "  ? No echo, doubling, or phasing effects" -ForegroundColor White
Write-Host "  ? Widget controls work (Play/Pause)" -ForegroundColor White
Write-Host "  ? Tray icon reflects correct state" -ForegroundColor White
Write-Host "  ? Only main app process plays audio" -ForegroundColor White
Write-Host "  ? Widget process only updates shared state`n" -ForegroundColor White

Write-Host "If audio still sounds doubled:" -ForegroundColor Yellow
Write-Host "  1. Check Debug output for 'COM server mode' messages" -ForegroundColor White
Write-Host "  2. Verify widget process does NOT call _player.Play()" -ForegroundColor White
Write-Host "  3. Ensure only ONE MediaPlayer is actually playing" -ForegroundColor White
Write-Host "  4. Kill all Trdo processes and try again:`n" -ForegroundColor White
Write-Host "     Get-Process -Name 'Trdo' | Stop-Process -Force`n" -ForegroundColor Cyan

Write-Host "Press any key to start testing..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`n?? Click Play in widget and listen carefully..." -ForegroundColor Yellow
Write-Host "?? Audio should be clear and NOT doubled!" -ForegroundColor Green
Write-Host "?? Listen for echo, phasing, or weird effects.`n" -ForegroundColor White
