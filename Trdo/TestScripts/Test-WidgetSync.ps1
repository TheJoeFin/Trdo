# Test Widget and Tray Icon Synchronization

Write-Host "`n=== Trdo Widget Sync Test ===" -ForegroundColor Cyan
Write-Host "This script helps test that widget and tray icon stay in sync`n" -ForegroundColor White

# Check if both processes can run
$processes = Get-Process -Name "Trdo" -ErrorAction SilentlyContinue

Write-Host "[1] Checking Running Processes..." -ForegroundColor Yellow
if ($processes) {
    Write-Host "  Found $($processes.Count) Trdo process(es) running:" -ForegroundColor Green
    foreach ($proc in $processes) {
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)").CommandLine
        Write-Host "    PID $($proc.Id): $cmdLine" -ForegroundColor White
    }
} else {
    Write-Host "  No Trdo processes running" -ForegroundColor Gray
}
Write-Host ""

Write-Host "[2] Test Scenario:" -ForegroundColor Yellow
Write-Host "  Step 1: Launch main app (tray icon should appear)" -ForegroundColor White
Write-Host "  Step 2: Open Widgets Board (Win + W)" -ForegroundColor White
Write-Host "  Step 3: Click widget Play/Pause button" -ForegroundColor White
Write-Host "  Step 4: Check if tray icon updates" -ForegroundColor White
Write-Host "  Expected: Tray icon changes appearance`n" -ForegroundColor Green

Write-Host "[3] What to Look For:" -ForegroundColor Yellow
Write-Host "  Playing:  Radio.ico (colored icon)" -ForegroundColor Green
Write-Host "  Paused:   Radio-Black.ico or Radio-White.ico (theme-based)`n" -ForegroundColor Gray

Write-Host "[4] Additional Tests:" -ForegroundColor Yellow
Write-Host "  • Click tray icon - widget should update" -ForegroundColor White
Write-Host "  • Press media key on keyboard - both should update" -ForegroundColor White
Write-Host "  • Open media overlay (Win+Alt+M) - should show 'Trdo Radio'`n" -ForegroundColor White

Write-Host "Press any key to launch main app..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Launch main app
$package = Get-AppxPackage -Name "*Trdo*"
if ($package) {
    Write-Host "`nLaunching Trdo..." -ForegroundColor Yellow
    Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
    Write-Host "Main app launched! Check system tray for icon.`n" -ForegroundColor Green
} else {
    Write-Host "`nError: Trdo is not installed!" -ForegroundColor Red
    Write-Host "Deploy the app first.`n" -ForegroundColor Yellow
}

Write-Host "Test Instructions:" -ForegroundColor Cyan
Write-Host "1. Confirm tray icon is visible" -ForegroundColor White
Write-Host "2. Open Widgets Board: Press Win + W" -ForegroundColor White
Write-Host "3. Add 'Trdo - Radio Player' widget if not already added" -ForegroundColor White
Write-Host "4. Click Play in the widget" -ForegroundColor White
Write-Host "5. Watch the tray icon - it should change appearance`n" -ForegroundColor White

Write-Host "Debugging:" -ForegroundColor Yellow
Write-Host "If tray icon doesn't update:" -ForegroundColor White
Write-Host "  • Check Debug output in Visual Studio" -ForegroundColor Gray
Write-Host "  • Look for '[RadioPlayerService] PlaybackStateChanged' messages" -ForegroundColor Gray
Write-Host "  • Look for '[App] UpdateTrayIconAsync' messages" -ForegroundColor Gray
Write-Host "  • Verify both processes are running (see output above)`n" -ForegroundColor Gray
