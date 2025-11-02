# Test Shared State Synchronization
# Verifies that widget and main app share state through ApplicationData

Write-Host "`n=== Trdo Shared State Test ===" -ForegroundColor Cyan
Write-Host "This verifies widget and main app use the same state storage`n" -ForegroundColor White

# Check if app is installed
$package = Get-AppxPackage -Name "*Trdo*"
if (-not $package) {
    Write-Host "Error: Trdo is not installed!" -ForegroundColor Red
    Write-Host "Deploy the app first.`n" -ForegroundColor Yellow
    exit
}

Write-Host "[1] Checking Shared State Storage..." -ForegroundColor Yellow

try {
    # Access ApplicationData for the package
    $packageFamilyName = $package.PackageFamilyName
    $localAppData = "$env:LOCALAPPDATA\Packages\$packageFamilyName\LocalState"
    
    Write-Host "  Package Family: $packageFamilyName" -ForegroundColor White
    Write-Host "  LocalState Path: $localAppData`n" -ForegroundColor White
    
    # Check if settings.dat exists (ApplicationData.LocalSettings)
    $settingsFile = "$localAppData\settings\settings.dat"
    if (Test-Path $settingsFile) {
        Write-Host "  ? Settings file exists" -ForegroundColor Green
        $fileInfo = Get-Item $settingsFile
        Write-Host "    Size: $($fileInfo.Length) bytes" -ForegroundColor White
        Write-Host "    Modified: $($fileInfo.LastWriteTime)`n" -ForegroundColor White
    } else {
        Write-Host "  ? Settings file not found (app hasn't saved state yet)" -ForegroundColor Yellow
        Write-Host "    This is normal if you haven't started playback yet`n" -ForegroundColor Gray
    }
} catch {
    Write-Host "  ? Failed to access shared state: $($_.Exception.Message)`n" -ForegroundColor Red
}

Write-Host "[2] Verifying Shared State Keys..." -ForegroundColor Yellow
Write-Host "  The following keys should be synchronized:" -ForegroundColor White
Write-Host "    • RadioIsPlaying (bool) - Current playback state" -ForegroundColor Gray
Write-Host "    • RadioCurrentStreamUrl (string) - Active station URL" -ForegroundColor Gray
Write-Host "    • RadioVolume (double) - Volume level" -ForegroundColor Gray
Write-Host "    • WatchdogEnabled (bool) - Stream watchdog status`n" -ForegroundColor Gray

Write-Host "[3] Test Procedure:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Step 1: Start widget (click Play)" -ForegroundColor Cyan
Write-Host "    ? Widget writes RadioIsPlaying = true to shared state" -ForegroundColor White
Write-Host ""
Write-Host "  Step 2: Launch main app" -ForegroundColor Cyan
Write-Host "    ? Main app reads RadioIsPlaying = true from shared state" -ForegroundColor White
Write-Host "    ? Tray icon shows 'Playing' state automatically" -ForegroundColor White
Write-Host ""
Write-Host "  Step 3: Click tray icon to pause" -ForegroundColor Cyan
Write-Host "    ? Main app writes RadioIsPlaying = false to shared state" -ForegroundColor White
Write-Host ""
Write-Host "  Step 4: Check widget" -ForegroundColor Cyan
Write-Host "    ? Widget reads RadioIsPlaying = false from shared state" -ForegroundColor White
Write-Host "    ? Widget shows 'Paused' state" -ForegroundColor White
Write-Host ""

Write-Host "[4] Manual State Inspection:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  You can monitor the shared state by watching the settings.dat file:" -ForegroundColor White
Write-Host "  Path: $settingsFile" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Watch for changes:" -ForegroundColor White
Write-Host "  Get-Item '$settingsFile' | Select-Object LastWriteTime" -ForegroundColor Cyan
Write-Host ""

Write-Host "[5] Debug Output:" -ForegroundColor Yellow
Write-Host "  When running with debugger, look for these messages:" -ForegroundColor White
Write-Host "    ? 'Updated shared IsPlaying state to: true'" -ForegroundColor Green
Write-Host "    ? 'Loaded shared stream URL: http://...'" -ForegroundColor Green
Write-Host "    ? 'Updated shared StreamUrl to: http://...'" -ForegroundColor Green
Write-Host "    ? 'IsPlaying state mismatch - Shared: true, Local: false'" -ForegroundColor Yellow
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "Expected Behavior:" -ForegroundColor Green
Write-Host "? Widget changes play state ? Main app reflects it immediately" -ForegroundColor White
Write-Host "? Main app changes play state ? Widget reflects it immediately" -ForegroundColor White
Write-Host "? Close and restart either process ? State is preserved" -ForegroundColor White
Write-Host "? Both processes always show the same play/pause state`n" -ForegroundColor White

Write-Host "Press any key to launch widget debugger..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nLaunching widget provider in debug mode..." -ForegroundColor Yellow
Write-Host "Set breakpoints in RadioPlayerService.cs:" -ForegroundColor White
Write-Host "  • LoadSharedState() - Line where it reads RadioIsPlaying" -ForegroundColor Gray
Write-Host "  • PlaybackStateChanged event - Line where it writes RadioIsPlaying" -ForegroundColor Gray
Write-Host "  • IsPlaying property getter - Line where it reads shared state`n" -ForegroundColor Gray

Write-Host "Instructions:" -ForegroundColor Cyan
Write-Host "1. Press F5 in Visual Studio with 'Trdo Widget Provider (Package)' profile" -ForegroundColor White
Write-Host "2. Add widget to Widgets Board" -ForegroundColor White
Write-Host "3. Click Play - your breakpoints should hit!" -ForegroundColor White
Write-Host "4. Step through to verify shared state is being read/written`n" -ForegroundColor White
