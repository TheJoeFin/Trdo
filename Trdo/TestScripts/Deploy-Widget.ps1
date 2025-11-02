# Deploy Trdo with Widget Support
# This script performs a clean deployment and verification

param(
    [string]$Configuration = "Debug",
    [string]$Platform = "ARM64"
)

$ErrorActionPreference = "Stop"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Trdo Widget Deployment Script" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$solutionDir = "D:\source\Trdo"
$projectDir = "$solutionDir\Trdo"
$appxPath = "$projectDir\bin\$Platform\$Configuration\net9.0-windows10.0.19041.0\win-$($Platform.ToLower())\AppX"

# Step 1: Uninstall existing package
Write-Host "[1/5] Removing existing Trdo installation..." -ForegroundColor Yellow
$existing = Get-AppxPackage -Name "*Trdo*"
if ($existing) {
    Remove-AppxPackage -Package $existing.PackageFullName
    Write-Host "? Uninstalled previous version`n" -ForegroundColor Green
} else {
    Write-Host "? No previous installation found`n" -ForegroundColor Green
}

# Step 2: Verify build output
Write-Host "[2/5] Verifying build output..." -ForegroundColor Yellow
if (Test-Path $appxPath) {
    Write-Host "? Build output found at: $appxPath" -ForegroundColor Green
    
    # Check for AppxManifest.xml
    $manifestPath = "$appxPath\AppxManifest.xml"
    if (Test-Path $manifestPath) {
        Write-Host "? AppxManifest.xml exists" -ForegroundColor Green
    } else {
        Write-Host "? AppxManifest.xml NOT found!" -ForegroundColor Red
        Write-Host "  Action: Build the project first`n" -ForegroundColor Yellow
        exit 1
    }
    
    # Check for Trdo.exe
    $exePath = "$appxPath\Trdo.exe"
    if (Test-Path $exePath) {
        Write-Host "? Trdo.exe exists" -ForegroundColor Green
    } else {
        Write-Host "? Trdo.exe NOT found!" -ForegroundColor Red
        exit 1
    }
    
    # Check for widget assets
    $widgetAssetsPath = "$appxPath\Widgets\Assets"
    if (Test-Path $widgetAssetsPath) {
        $assets = Get-ChildItem $widgetAssetsPath -File
        Write-Host "? Widget assets folder exists with $($assets.Count) files" -ForegroundColor Green
    } else {
        Write-Host "? Widget assets folder NOT found!" -ForegroundColor Red
        Write-Host "  Expected: $widgetAssetsPath" -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host ""
} else {
    Write-Host "? Build output not found!" -ForegroundColor Red
    Write-Host "  Expected: $appxPath" -ForegroundColor Yellow
    Write-Host "  Action: Build the project first (Configuration=$Configuration, Platform=$Platform)`n" -ForegroundColor Yellow
    exit 1
}

# Step 3: Register package
Write-Host "[3/5] Registering app package..." -ForegroundColor Yellow
try {
    Add-AppxPackage -Register "$appxPath\AppxManifest.xml" -ForceApplicationShutdown
    Write-Host "? Package registered successfully`n" -ForegroundColor Green
} catch {
    Write-Host "? Failed to register package!" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)`n" -ForegroundColor Red
    exit 1
}

# Step 4: Verify installation
Write-Host "[4/5] Verifying installation..." -ForegroundColor Yellow
Start-Sleep -Seconds 2  # Give Windows a moment to register
$package = Get-AppxPackage -Name "*Trdo*"
if ($package) {
    Write-Host "? Package installed:" -ForegroundColor Green
    Write-Host "  Name: $($package.Name)" -ForegroundColor White
    Write-Host "  Version: $($package.Version)" -ForegroundColor White
    Write-Host "  PackageFamilyName: $($package.PackageFamilyName)`n" -ForegroundColor White
} else {
    Write-Host "? Package not found after installation!" -ForegroundColor Red
    exit 1
}

# Step 5: Test COM server manually
Write-Host "[5/5] Testing COM Server..." -ForegroundColor Yellow
Write-Host "  You can manually test the COM server with:" -ForegroundColor White
Write-Host "  & '$($package.InstallLocation)\Trdo.exe' -RegisterProcessAsComServer" -ForegroundColor Cyan
Write-Host "  (Press Ctrl+C to stop the COM server after testing)`n" -ForegroundColor Gray

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Close any open Widgets Board" -ForegroundColor White
Write-Host "2. Press Win + W to open Widgets Board" -ForegroundColor White
Write-Host "3. Click 'Add widgets' (+ icon in top right)" -ForegroundColor White
Write-Host "4. Scroll down to find 'Trdo - Radio Player'" -ForegroundColor White
Write-Host "5. Click it to add to your board`n" -ForegroundColor White

Write-Host "Troubleshooting:" -ForegroundColor Yellow
Write-Host "If widget doesn't appear, run: .\Debug-WidgetRegistration.ps1`n" -ForegroundColor White
