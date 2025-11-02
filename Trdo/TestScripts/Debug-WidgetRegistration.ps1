# Trdo Widget Registration Debugger
# This script helps diagnose why widgets aren't appearing in the Widgets Board

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Trdo Widget Registration Debugger" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# 1. Check if package is installed
Write-Host "[1] Checking Package Installation..." -ForegroundColor Yellow
$package = Get-AppxPackage -Name "*Trdo*"
if ($package) {
    Write-Host "? Package Found:" -ForegroundColor Green
    Write-Host "  Name: $($package.Name)" -ForegroundColor White
    Write-Host "  Version: $($package.Version)" -ForegroundColor White
    Write-Host "  PackageFamilyName: $($package.PackageFamilyName)" -ForegroundColor White
    Write-Host "  InstallLocation: $($package.InstallLocation)`n" -ForegroundColor White
} else {
    Write-Host "? Trdo package is NOT installed!" -ForegroundColor Red
    Write-Host "  Action: Deploy the app from Visual Studio (Right-click project > Deploy)`n" -ForegroundColor Yellow
    exit
}

# 2. Check app extensions
Write-Host "[2] Checking Widget Extension Registration..." -ForegroundColor Yellow

# Read the actual manifest XML file directly instead of using Get-AppxPackageManifest
$manifestPath = Join-Path $package.InstallLocation "AppxManifest.xml"
if (Test-Path $manifestPath) {
    [xml]$manifest = Get-Content $manifestPath
    
    # Define namespace manager for XML with namespaces
    $ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $ns.AddNamespace("app", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $ns.AddNamespace("uap3", "http://schemas.microsoft.com/appx/manifest/uap/windows10/3")
    $ns.AddNamespace("com", "http://schemas.microsoft.com/appx/manifest/com/windows10")
    
    # Check for widget extension
    $widgetExtension = $manifest.SelectSingleNode("//uap3:Extension[@Category='windows.appExtension']", $ns)
    if ($widgetExtension) {
        Write-Host "? Widget Extension Found" -ForegroundColor Green
        $appExtension = $widgetExtension.AppExtension
        Write-Host "  Extension Name: $($appExtension.Name)" -ForegroundColor White
        Write-Host "  Extension ID: $($appExtension.Id)" -ForegroundColor White
        Write-Host "  Display Name: $($appExtension.DisplayName)" -ForegroundColor White
        Write-Host "  Public Folder: $($appExtension.PublicFolder)" -ForegroundColor White
        
        # Check widget definition
        $widgetDef = $widgetExtension.AppExtension.Properties.WidgetProvider.Definitions.Definition
        if ($widgetDef) {
            Write-Host "  Widget ID: $($widgetDef.Id)" -ForegroundColor White
            Write-Host "  Widget Name: $($widgetDef.DisplayName)" -ForegroundColor White
        }
        Write-Host ""
    } else {
        Write-Host "? No widget extension found in manifest!" -ForegroundColor Red
        Write-Host "  Action: Check Package.appxmanifest for uap3:Extension with Category='windows.appExtension'`n" -ForegroundColor Yellow
    }
} else {
    Write-Host "? AppxManifest.xml not found at: $manifestPath" -ForegroundColor Red
    Write-Host ""
}

# 3. Check COM server registration
Write-Host "[3] Checking COM Server Registration..." -ForegroundColor Yellow
if (Test-Path $manifestPath) {
    [xml]$manifest = Get-Content $manifestPath
    
    $ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $ns.AddNamespace("app", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $ns.AddNamespace("com", "http://schemas.microsoft.com/appx/manifest/com/windows10")
    
    $comExtension = $manifest.SelectSingleNode("//com:Extension[@Category='windows.comServer']", $ns)
    if ($comExtension) {
        Write-Host "? COM Server Extension Found" -ForegroundColor Green
        $exeServer = $comExtension.ComServer.ExeServer
        Write-Host "  Executable: $($exeServer.Executable)" -ForegroundColor White
        Write-Host "  Arguments: $($exeServer.Arguments)" -ForegroundColor White
        Write-Host "  Display Name: $($exeServer.DisplayName)" -ForegroundColor White
        Write-Host "  Class ID: $($exeServer.Class.Id)`n" -ForegroundColor White
    } else {
        Write-Host "? No COM server registration found!" -ForegroundColor Red
        Write-Host "  Action: Check Package.appxmanifest for com:Extension with Category='windows.comServer'`n" -ForegroundColor Yellow
    }
} else {
    Write-Host "? AppxManifest.xml not found!" -ForegroundColor Red
    Write-Host ""
}

# 4. Check if widget assets exist
Write-Host "[4] Checking Widget Assets..." -ForegroundColor Yellow
$installLocation = $package.InstallLocation
$widgetsFolder = Join-Path $installLocation "Widgets\Assets"

if (Test-Path $widgetsFolder) {
    Write-Host "? Widgets folder found: $widgetsFolder" -ForegroundColor Green
    $assets = Get-ChildItem $widgetsFolder -File
    Write-Host "  Assets in folder:" -ForegroundColor White
    foreach ($asset in $assets) {
        Write-Host "    - $($asset.Name) ($([math]::Round($asset.Length/1KB, 2)) KB)" -ForegroundColor White
    }
    Write-Host ""
} else {
    Write-Host "? Widgets folder NOT found!" -ForegroundColor Red
    Write-Host "  Expected: $widgetsFolder" -ForegroundColor Yellow
    Write-Host "  Action: Ensure widget assets are included in the project with CopyToOutputDirectory=PreserveNewest`n" -ForegroundColor Yellow
}

# 5. Check if template file exists
Write-Host "[5] Checking Widget Template..." -ForegroundColor Yellow
$templatePath = Join-Path $installLocation "Widgets\Templates\RadioPlayerWidgetTemplate.json"
if (Test-Path $templatePath) {
    Write-Host "? Widget template found" -ForegroundColor Green
    $templateSize = (Get-Item $templatePath).Length
    Write-Host "  Path: $templatePath" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($templateSize/1KB, 2)) KB`n" -ForegroundColor White
} else {
    Write-Host "? Widget template NOT found!" -ForegroundColor Red
    Write-Host "  Expected: $templatePath" -ForegroundColor Yellow
    Write-Host "  Action: Ensure template is included with CopyToOutputDirectory=PreserveNewest`n" -ForegroundColor Yellow
}

# 6. Test COM server launch
Write-Host "[6] Testing COM Server Launch..." -ForegroundColor Yellow
$exePath = Join-Path $installLocation "Trdo.exe"
if (Test-Path $exePath) {
    Write-Host "? Trdo.exe found at: $exePath" -ForegroundColor Green
    Write-Host "  You can manually test COM server with:" -ForegroundColor White
    Write-Host "  & '$exePath' -RegisterProcessAsComServer`n" -ForegroundColor Cyan
} else {
    Write-Host "? Trdo.exe NOT found!" -ForegroundColor Red
    Write-Host "  Expected: $exePath`n" -ForegroundColor Yellow
}

# 7. Check Event Viewer for widget errors
Write-Host "[7] Checking Recent Widget Errors..." -ForegroundColor Yellow
try {
    $events = Get-WinEvent -LogName "Microsoft-Windows-TWinUI/Operational" -MaxEvents 10 -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like "*Trdo*" -or $_.Message -like "*D5A5B8F2-9C3A-4E1B-8F7D-6A4C3B2E1D9F*" }
    
    if ($events) {
        Write-Host "? Found widget-related events:" -ForegroundColor Yellow
        foreach ($event in $events) {
            Write-Host "  Level: $($event.LevelDisplayName) | Time: $($event.TimeCreated)" -ForegroundColor White
            Write-Host "  Message: $($event.Message)`n" -ForegroundColor White
        }
    } else {
        Write-Host "? No recent widget errors found in Event Viewer" -ForegroundColor Green
        Write-Host "  (This doesn't mean everything is working - it just means no errors were logged)`n" -ForegroundColor Gray
    }
} catch {
    Write-Host "? Could not read Event Viewer (may require admin privileges)" -ForegroundColor Gray
    Write-Host "  To check manually: Event Viewer > Applications and Services Logs > Microsoft > Windows > Apps > Microsoft-Windows-TWinUI/Operational`n" -ForegroundColor Gray
}

# 8. Check Widgets Board state
Write-Host "[8] Widget Board Recommendations..." -ForegroundColor Yellow
Write-Host "  To see your widget:" -ForegroundColor White
Write-Host "  1. Close Widgets Board completely (if open)" -ForegroundColor Cyan
Write-Host "  2. Press Win + W to open Widgets Board" -ForegroundColor Cyan
Write-Host "  3. Click 'Add widgets' (+ icon)" -ForegroundColor Cyan
Write-Host "  4. Scroll to bottom and look for 'Trdo - Radio Player'" -ForegroundColor Cyan
Write-Host "  5. If not found, try: Uninstall app, clean, rebuild, deploy`n" -ForegroundColor Cyan

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Debugging Complete!" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Summary
Write-Host "SUMMARY:" -ForegroundColor Yellow
if ($package -and $widgetExtension -and $comExtension) {
    Write-Host "? All core components are registered correctly" -ForegroundColor Green
    Write-Host "  If widget still doesn't appear:" -ForegroundColor Yellow
    Write-Host "  1. Completely uninstall: Get-AppxPackage *Trdo* | Remove-AppxPackage" -ForegroundColor White
    Write-Host "  2. Clean solution in Visual Studio" -ForegroundColor White
    Write-Host "  3. Rebuild and Deploy" -ForegroundColor White
    Write-Host "  4. Restart Widgets Board (close and reopen)`n" -ForegroundColor White
} else {
    Write-Host "? Some components are missing - review the errors above" -ForegroundColor Red
    Write-Host "  Fix the issues and redeploy the app`n" -ForegroundColor Yellow
}
