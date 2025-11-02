# Test Watchdog and State Sync Fixes
# Verifies that watchdog respects widget pause and state stays in sync

Write-Host "`n????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  Trdo Watchdog & State Sync Fix Verification            ?" -ForegroundColor Cyan
Write-Host "????????????????????????????????????????????????????????????`n" -ForegroundColor Cyan

Write-Host "This test verifies the following fixes:" -ForegroundColor Yellow
Write-Host "  1. Watchdog respects pause from widget (doesn't try to resume)" -ForegroundColor White
Write-Host "  2. Tray icon updates within 2 seconds of widget state change" -ForegroundColor White
Write-Host "  3. Both processes always show consistent play/pause state`n" -ForegroundColor White

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[TEST 1] Watchdog Respects Widget Pause" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Launch main app (tray icon)" -ForegroundColor Gray
Write-Host "    2. Add widget to Widgets Board" -ForegroundColor Gray
Write-Host "    3. Click Play in widget" -ForegroundColor Gray
Write-Host "    4. Wait 5 seconds" -ForegroundColor Gray
Write-Host "    5. Click Pause in widget" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected Behavior:" -ForegroundColor Cyan
Write-Host "    ? Main app detects widget pause" -ForegroundColor Green
Write-Host "    ? Tray icon updates to 'Paused' state" -ForegroundColor Green
Write-Host "    ? Watchdog sees shared state = paused" -ForegroundColor Green
Write-Host "    ? Watchdog does NOT try to resume playback" -ForegroundColor Green
Write-Host "    ? Radio stays paused" -ForegroundColor Green
Write-Host ""
Write-Host "  Debug Messages to Look For:" -ForegroundColor White
Write-Host "    • '[Watchdog] Detected pause by another process - disabling recovery'" -ForegroundColor Gray
Write-Host "    • '[App] Shared state changed: IsPlaying True ? False'" -ForegroundColor Gray
Write-Host "    • '[RadioPlayerService] IsPlaying state mismatch - Shared: False, Local: True'" -ForegroundColor Gray
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[TEST 2] Tray Icon Updates Quickly" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Setup:" -ForegroundColor White
Write-Host "    1. Both main app and widget running" -ForegroundColor Gray
Write-Host "    2. Radio is paused" -ForegroundColor Gray
Write-Host "    3. Click Play in widget" -ForegroundColor Gray
Write-Host "    4. Watch the tray icon" -ForegroundColor Gray
Write-Host ""
Write-Host "  Expected Behavior:" -ForegroundColor Cyan
Write-Host "    ? Within 2 seconds: Tray icon changes to Radio.ico (playing)" -ForegroundColor Green
Write-Host "    ? Tooltip updates to 'Trdo (Playing) - Click to Pause'" -ForegroundColor Green
Write-Host ""
Write-Host "  Timing:" -ForegroundColor White
Write-Host "    • Polling interval: 2 seconds" -ForegroundColor Gray
Write-Host "    • Maximum delay: 2 seconds" -ForegroundColor Gray
Write-Host "    • Typical delay: 0.5-1.5 seconds" -ForegroundColor Gray
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[TEST 3] State Stays Consistent" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Scenario A: Widget ? Main App" -ForegroundColor White
Write-Host "    1. Click Play in widget" -ForegroundColor Gray
Write-Host "    2. Wait 3 seconds" -ForegroundColor Gray
Write-Host "    3. Check tray icon" -ForegroundColor Gray
Write-Host "    Expected: ? Shows 'Playing' state (Radio.ico)" -ForegroundColor Green
Write-Host ""
Write-Host "  Scenario B: Main App ? Widget" -ForegroundColor White
Write-Host "    1. Click tray icon to play" -ForegroundColor Gray
Write-Host "    2. Wait 1 second" -ForegroundColor Gray
Write-Host "    3. Check widget" -ForegroundColor Gray
Write-Host "    Expected: ? Shows '? Pause' button" -ForegroundColor Green
Write-Host ""
Write-Host "  Scenario C: Rapid Toggle" -ForegroundColor White
Write-Host "    1. Click Play in widget" -ForegroundColor Gray
Write-Host "    2. Immediately click Pause in widget" -ForegroundColor Gray
Write-Host "    3. Immediately click Play in widget" -ForegroundColor Gray
Write-Host "    4. Wait 3 seconds" -ForegroundColor Gray
Write-Host "    Expected: ? Both widget and tray show 'Playing' state" -ForegroundColor Green
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[DEBUG] Key Code Changes" -ForegroundColor Yellow
Write-Host ""
Write-Host "  StreamWatchdogService.cs:" -ForegroundColor White
Write-Host "    • Now checks shared state before attempting recovery" -ForegroundColor Gray
Write-Host "    • Detects when another process paused playback" -ForegroundColor Gray
Write-Host "    • Disables recovery when shared state = paused" -ForegroundColor Gray
Write-Host ""
Write-Host "  App.xaml.cs:" -ForegroundColor White
Write-Host "    • Added shared state polling timer (2 second interval)" -ForegroundColor Gray
Write-Host "    • Compares current state with last known state" -ForegroundColor Gray
Write-Host "    • Triggers UI update when state changes detected" -ForegroundColor Gray
Write-Host ""
Write-Host "  RadioPlayerService.cs:" -ForegroundColor White
Write-Host "    • IsPlaying always returns shared state when mismatched" -ForegroundColor Gray
Write-Host "    • Ensures consistency across processes" -ForegroundColor Gray
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "[MANUAL TEST PROCEDURE]`n" -ForegroundColor Yellow

Write-Host "Step 1: Deploy and Launch" -ForegroundColor Cyan
Write-Host "  • Rebuild: Build > Rebuild Solution" -ForegroundColor White
Write-Host "  • Deploy: Right-click Trdo project > Deploy" -ForegroundColor White
Write-Host "  • Launch: Start Trdo from Start Menu" -ForegroundColor White
Write-Host "  • Verify: Tray icon appears in system tray`n" -ForegroundColor White

Write-Host "Step 2: Add Widget" -ForegroundColor Cyan
Write-Host "  • Press Win + W to open Widgets Board" -ForegroundColor White
Write-Host "  • Click 'Add widgets' (+)" -ForegroundColor White
Write-Host "  • Add 'Trdo - Radio Player' widget" -ForegroundColor White
Write-Host "  • Verify: Widget appears on board`n" -ForegroundColor White

Write-Host "Step 3: Test Watchdog Fix (Critical!)" -ForegroundColor Cyan
Write-Host "  • Click Play in widget" -ForegroundColor White
Write-Host "  • Wait 10 seconds (let watchdog activate)" -ForegroundColor White
Write-Host "  • Click Pause in widget" -ForegroundColor White
Write-Host "  • Wait 10 seconds and observe:" -ForegroundColor White
Write-Host "    ? Radio stays paused (watchdog respects widget pause)" -ForegroundColor Green
Write-Host "    ? If radio resumes automatically, watchdog fix failed!" -ForegroundColor Red
Write-Host ""

Write-Host "Step 4: Test Tray Icon Sync" -ForegroundColor Cyan
Write-Host "  • With widget paused, click Play in widget" -ForegroundColor White
Write-Host "  • Count seconds: 1... 2..." -ForegroundColor White
Write-Host "  • Verify tray icon changed to Radio.ico within 2 seconds" -ForegroundColor White
Write-Host "  • Repeat with Pause button" -ForegroundColor White
Write-Host "  • Verify tray icon changed to Radio-Black/White.ico`n" -ForegroundColor White

Write-Host "Step 5: Test Bidirectional Sync" -ForegroundColor Cyan
Write-Host "  • Click tray icon to toggle play/pause" -ForegroundColor White
Write-Host "  • Verify widget button updates" -ForegroundColor White
Write-Host "  • Click widget button to toggle" -ForegroundColor White
Write-Host "  • Verify tray icon updates`n" -ForegroundColor White

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "Expected Improvements:" -ForegroundColor Green
Write-Host "  ? Watchdog no longer fights with widget pause" -ForegroundColor White
Write-Host "  ? Tray icon updates within 2 seconds (previously could be stuck)" -ForegroundColor White
Write-Host "  ? State consistency maintained across processes" -ForegroundColor White
Write-Host "  ? No more 'phantom resume' after widget pause" -ForegroundColor White
Write-Host ""

Write-Host "If Issues Persist:" -ForegroundColor Yellow
Write-Host "  1. Check Debug output for polling messages" -ForegroundColor White
Write-Host "  2. Verify shared state is being written:" -ForegroundColor White
Write-Host "     Get-ItemProperty -Path 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\40087JoeFinApps.Trdo_*\PersistedStorageItemTable\ManagedByFramework' -Name RadioIsPlaying" -ForegroundColor Gray
Write-Host "  3. Increase polling frequency (change 2 seconds to 1 second in App.xaml.cs)" -ForegroundColor White
Write-Host "  4. Check Event Viewer for crashes or errors`n" -ForegroundColor White

Write-Host "Press any key to start debugging session..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nReady to debug! Set breakpoints at:" -ForegroundColor Yellow
Write-Host "  • StreamWatchdogService.CheckStreamHealthAsync() - Line checking shared state" -ForegroundColor White
Write-Host "  • App.CheckSharedState() - Line comparing states" -ForegroundColor White
Write-Host "  • RadioPlayerService.IsPlaying getter - Line returning shared state`n" -ForegroundColor White
