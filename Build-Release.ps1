param([Parameter(Mandatory=$true)][string]$IsccPath)
$ErrorActionPreference='Stop'
$version=(Get-Content (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION'}
if($PSVersionTable.PSEdition -eq 'Core'){throw 'Run this script with Windows PowerShell 5.1'}
& (Join-Path $PSScriptRoot 'Build.ps1')
& (Join-Path $PSScriptRoot 'Test-App.ps1')
& (Join-Path $PSScriptRoot 'Test-Configuration.ps1')
$payload=Join-Path $PSScriptRoot 'dist'
$release=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $payload,$release -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'LaptopPower.exe'),(Join-Path $PSScriptRoot 'README.txt'),(Join-Path $PSScriptRoot 'LICENSE') $payload -Force
$zip=Join-Path $release "LaptopPower-v$version-win-x64.zip"
Compress-Archive -Path (Join-Path $payload 'LaptopPower.exe'),(Join-Path $payload 'README.txt'),(Join-Path $payload 'LICENSE') -DestinationPath $zip -Force
& $IsccPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/DReleaseDir=$release" (Join-Path $PSScriptRoot 'installer\Setup.iss')
if($LASTEXITCODE -ne 0){throw 'Setup compilation failed'}
$setup=Join-Path $release "LaptopPower-Setup-$version-win-x64.exe"
@($setup,$zip) | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetFileName($_) } | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Release artifacts: $release"
