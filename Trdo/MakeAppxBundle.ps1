# go to the packages folder and copy all .msix files to a new folder called "msixes"
# then run the MakeAppxBundle command to create a bundle
# the bundle will be created in the same folder as the script
# the bundle will be called "Trdo.msixbundle"

# get the latest version which has been created using a regex to match something like 1.0.3.0
# Function to compare two version numbers
function Compare-Version {
    param (
        [string]$version1,
        [string]$version2
    )

    $v1Parts = $version1 -split '\.'
    $v2Parts = $version2 -split '\.'

    for ($i = 0; $i -lt $v1Parts.Length; $i++) {
        if ([int]$v1Parts[$i] -gt [int]$v2Parts[$i]) {
            return 1
        }
        elseif ([int]$v1Parts[$i] -lt [int]$v2Parts[$i]) {
            return -1
        }
    }
    return 0
}

# Function to extract and find the largest version number from file names
function Find-LargestVersion {
    param (
        [string[]]$fileNames
    )

    $largestVersion = "0.0.0.0"

    foreach ($fileName in $fileNames) {
        if ($fileName -match '\d+\.\d+\.\d+\.\d+') {
            $version = $matches[0]
            $compareResult = Compare-Version $version $largestVersion
            if ($compareResult -gt 0) {
                $largestVersion = $version
            }
        }
    }

    return $largestVersion
}

# Find out what the largest version number is and copy those MSIX files to a new folder
$fileNames = Get-ChildItem -Path .\AppPackages | Select-Object -ExpandProperty Name

$largestVersion = Find-LargestVersion -fileNames $fileNames
Write-Output "The largest version number is: $largestVersion"

# Create a new folder called "msixes" if it doesn't already exist
$msixesFolder = ".\AppPackages\msixes_" + $largestVersion
if (-not (Test-Path -Path $msixesFolder)) {
    New-Item -Path $msixesFolder -ItemType Directory
}
else {
    # delete all files in the folder
    # if there are other files in the folder the makeAppx will fail
    Remove-Item -Path "$msixesFolder\*" -Force
}

$folderNames = @()
$folderNames += "Trdo_" + $largestVersion + "_x64_Test"
$folderNames += "Trdo_" + $largestVersion + "_arm64_Test"

# copy all .msix files in each folder to the msixes folder
foreach ($folderName in $folderNames) {
    $sourcePath = ".\AppPackages\$folderName"
    Copy-Item -Path "$sourcePath\*.msix" -Destination $msixesFolder -Force
}

$currentPath = (Get-Location).Path
$trimmedMsixesFolder = $msixesFolder.TrimStart(".\\")
$locationOfMakeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
$locationOfMsixes = "${currentPath}\${trimmedMsixesFolder}"
$bundleName = "${currentPath}\${trimmedMsixesFolder}\Trdo_${largestVersion}.msixbundle"

& $locationOfMakeAppx bundle /d $locationOfMsixes /p  $bundleName

# maybe run appcert too
# appcert.exe reset
# appcert test -apptype desktop -setuppath d:\cdrom\setup.exe -appusage peruser -reportoutputpath [report file name]# appcert test -apptype desktop -setuppath d:\cdrom\setup.exe -appusage peruser -reportoutputpath [report file name]