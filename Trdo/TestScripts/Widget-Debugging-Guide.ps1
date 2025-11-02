# Complete Widget Debugging Guide for Trdo
# Run this after making changes to Package.appxmanifest

Write-Host @"

????????????????????????????????????????????????????????????????
?          Trdo Widget Debugging Checklist                    ?
????????????????????????????????????????????????????????????????

"@ -ForegroundColor Cyan

Write-Host "ISSUE: Widget not appearing in Widgets Board`n" -ForegroundColor Yellow

Write-Host "DEBUGGING STEPS:`n" -ForegroundColor White

Write-Host "Step 1: Clean Build" -ForegroundColor Green
Write-Host "   In Visual Studio:" -ForegroundColor White
Write-Host "   • Build > Clean Solution" -ForegroundColor Gray
Write-Host "   • Delete bin\ and obj\ folders manually if needed" -ForegroundColor Gray
Write-Host ""

Write-Host "Step 2: Rebuild" -ForegroundColor Green  
Write-Host "   In Visual Studio:" -ForegroundColor White
Write-Host "   • Build > Rebuild Solution" -ForegroundColor Gray
Write-Host "   • Ensure build succeeds with no errors" -ForegroundColor Gray
Write-Host ""

Write-Host "Step 3: Uninstall Previous Version" -ForegroundColor Green
Write-Host "   Run in PowerShell:" -ForegroundColor White
Write-Host "   Get-AppxPackage *Trdo* | Remove-AppxPackage" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 4: Deploy (CRITICAL - not just Build!)" -ForegroundColor Green
Write-Host "   In Visual Studio:" -ForegroundColor White
Write-Host "   • Right-click 'Trdo' project in Solution Explorer" -ForegroundColor Gray
Write-Host "   • Click 'Deploy'" -ForegroundColor Gray
Write-Host "   • Wait for 'Deploy succeeded' message" -ForegroundColor Gray
Write-Host "   OR" -ForegroundColor Yellow
Write-Host "   • Press F5 to debug (which also deploys)" -ForegroundColor Gray
Write-Host ""

Write-Host "Step 5: Verify Deployment" -ForegroundColor Green
Write-Host "   Run Debug-WidgetRegistration.ps1 script" -ForegroundColor Cyan
Write-Host "   • Should show 'Widget Extension Found'" -ForegroundColor Gray
Write-Host "   • Should show 'COM Server Extension Found'" -ForegroundColor Gray
Write-Host ""

Write-Host "Step 6: Test Widget" -ForegroundColor Green
Write-Host "   • Close any open Widgets Board (important!)" -ForegroundColor Gray
Write-Host "   • Press Win + W" -ForegroundColor Gray
Write-Host "   • Click 'Add widgets' (+ icon)" -ForegroundColor Gray
Write-Host "   • Scroll to bottom" -ForegroundColor Gray
Write-Host "   • Look for 'Trdo - Radio Player'" -ForegroundColor Gray
Write-Host ""

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "COMMON ISSUES & SOLUTIONS:`n" -ForegroundColor Yellow

Write-Host "Issue: 'No widget extension found in manifest'" -ForegroundColor Red
Write-Host "   Cause: Built but not deployed" -ForegroundColor White
Write-Host "   Solution: Must use Deploy (not just Build)!`n" -ForegroundColor Green

Write-Host "Issue: Widget appears but icon is missing" -ForegroundColor Red
Write-Host "   Cause: Asset paths don't match PublicFolder" -ForegroundColor White
Write-Host "   Solution: Check PublicFolder='Widgets' and paths are Assets\filename.png`n" -ForegroundColor Green

Write-Host "Issue: Widget doesn't respond to button clicks" -ForegroundColor Red
Write-Host "   Cause: COM server not running or crashed" -ForegroundColor White
Write-Host "   Solution: Check Event Viewer for errors, test COM server manually`n" -ForegroundColor Green

Write-Host "Issue: Changes to manifest not reflected" -ForegroundColor Red
Write-Host "   Cause: Cached build output" -ForegroundColor White
Write-Host "   Solution: Clean + Rebuild + Uninstall + Deploy`n" -ForegroundColor Green

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "MANUAL COM SERVER TEST:`n" -ForegroundColor Yellow
Write-Host "To test if the COM server starts correctly:" -ForegroundColor White
Write-Host "1. Deploy the app" -ForegroundColor Gray
Write-Host "2. Run:" -ForegroundColor Gray
$package = Get-AppxPackage -Name "*Trdo*"
if ($package) {
    Write-Host "   & '$($package.InstallLocation)\Trdo.exe' -RegisterProcessAsComServer" -ForegroundColor Cyan
    Write-Host "3. App should start and stay running (no UI)" -ForegroundColor Gray
    Write-Host "4. Check Task Manager for Trdo.exe process" -ForegroundColor Gray
    Write-Host "5. Press Ctrl+C to stop`n" -ForegroundColor Gray
} else {
    Write-Host "   (App not installed - deploy first)`n" -ForegroundColor Yellow
}

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "DEBUG WIDGET PROVIDER:`n" -ForegroundColor Yellow
Write-Host "To debug the widget provider code:" -ForegroundColor White
Write-Host "1. In Visual Studio, select launch profile:" -ForegroundColor Gray
Write-Host "   'Trdo Widget Provider (Package)'" -ForegroundColor Cyan
Write-Host "2. Set breakpoints in:" -ForegroundColor Gray
Write-Host "   • TrdoWidgetProvider.cs" -ForegroundColor White
Write-Host "   • RadioPlayerWidget.cs" -ForegroundColor White
Write-Host "3. Press F5 to start debugging" -ForegroundColor Gray
Write-Host "4. Add widget to Widgets Board" -ForegroundColor Gray
Write-Host "5. Breakpoints should hit`n" -ForegroundColor Gray

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "QUICK COMMANDS:`n" -ForegroundColor Yellow
Write-Host "Uninstall:  " -NoNewline -ForegroundColor White
Write-Host "Get-AppxPackage *Trdo* | Remove-AppxPackage" -ForegroundColor Cyan
Write-Host "Check Install:  " -NoNewline -ForegroundColor White  
Write-Host "Get-AppxPackage *Trdo*" -ForegroundColor Cyan
Write-Host "Debug Check:  " -NoNewline -ForegroundColor White
Write-Host ".\Debug-WidgetRegistration.ps1" -ForegroundColor Cyan
Write-Host "Deploy:  " -NoNewline -ForegroundColor White
Write-Host ".\Deploy-Widget.ps1`n" -ForegroundColor Cyan

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Check current state
$currentPackage = Get-AppxPackage -Name "*Trdo*"
Write-Host "CURRENT STATE:" -ForegroundColor Yellow
if ($currentPackage) {
    Write-Host "  ? Trdo is installed (version $($currentPackage.Version))" -ForegroundColor Green
    Write-Host "  Next: Close Widgets Board and try adding the widget`n" -ForegroundColor White
} else {
    Write-Host "  ? Trdo is NOT installed" -ForegroundColor Red
    Write-Host "  Next: Deploy from Visual Studio or run .\Deploy-Widget.ps1`n" -ForegroundColor Yellow
}
