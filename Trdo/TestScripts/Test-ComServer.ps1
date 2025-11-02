# Test Trdo COM Server
# This script launches the widget provider COM server for manual testing

$package = Get-AppxPackage -Name "*Trdo*"
if (-not $package) {
    Write-Host "Error: Trdo is not installed!" -ForegroundColor Red
    Write-Host "Deploy the app first." -ForegroundColor Yellow
    exit
}

$exePath = Join-Path $package.InstallLocation "Trdo.exe"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Trdo COM Server Manual Test" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "Launching COM server..." -ForegroundColor Yellow
Write-Host "Path: $exePath" -ForegroundColor Gray
Write-Host "Args: -RegisterProcessAsComServer`n" -ForegroundColor Gray

Write-Host "What should happen:" -ForegroundColor Yellow
Write-Host "  1. App starts (no window/tray icon should appear)" -ForegroundColor White
Write-Host "  2. Process stays running" -ForegroundColor White
Write-Host "  3. Check Task Manager for 'Trdo.exe' process" -ForegroundColor White
Write-Host "  4. Widget host can now create widgets`n" -ForegroundColor White

Write-Host "Press Ctrl+C to stop the COM server`n" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

try {
    & $exePath -RegisterProcessAsComServer
} catch {
    Write-Host "`nError launching COM server:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "COM Server stopped" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
