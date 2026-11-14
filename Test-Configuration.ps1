$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'Configuration.cs') -ReferencedAssemblies System.Web.Extensions
$folder=Join-Path $PSScriptRoot 'test-data'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$path=Join-Path $folder 'custom-config.json'
$config=[PowerConfig]::Defaults()
$mode=$config.modes[0]
$mode.pluggedIn.cpuMinimum=9;$mode.pluggedIn.cpuMaximum=73;$mode.battery.cpuMinimum=7;$mode.battery.cpuMaximum=46
$mode.pluggedIn.efficiencyPreference=20;$mode.battery.efficiencyPreference=90
$mode.pluggedIn.boostMode=1;$mode.battery.boostMode=0
$mode.pluggedIn.screenBrightness=63;$mode.battery.screenBrightness=42
$mode.pluggedIn.displayTimeoutMinutes=8;$mode.battery.displayTimeoutMinutes=2
$mode.pluggedIn.sleepTimeoutMinutes=35;$mode.battery.sleepTimeoutMinutes=11
[PowerConfig]::Save($path,$config)
$loaded=[PowerConfig]::Load($path)
if($loaded.modes[0].pluggedIn.cpuMaximum -ne 73 -or $loaded.modes[0].battery.screenBrightness -ne 42){throw 'Configuration round-trip failed.'}
'PASS: custom per-source configuration round-trip.'
$legacy = [IO.File]::ReadAllText($path) | ConvertFrom-Json
foreach ($profile in $legacy.modes) {
    foreach ($property in 'nvidiaBattery','nvidiaUsb','nvidiaBarrel') { $profile.PSObject.Properties.Remove($property) }
}
$legacyPath = Join-Path $folder 'legacy-gpu-config.json'
[IO.File]::WriteAllText($legacyPath, ($legacy | ConvertTo-Json -Depth 10))
$legacyLoaded = [PowerConfig]::Load($legacyPath)
foreach ($profile in $legacyLoaded.modes) {
    if ($profile.nvidiaBattery -ne 0 -or $profile.nvidiaUsb -ne 0 -or $profile.nvidiaBarrel -ne 0) { throw 'Legacy profile could change NVIDIA state.' }
}
$legacyLoaded.modes[0].nvidiaBattery=2
$legacyLoaded.modes[0].nvidiaUsb=1
[PowerConfig]::Save($legacyPath,$legacyLoaded)
$gpu=[PowerConfig]::Load($legacyPath)
if ($gpu.modes[0].nvidiaBattery -ne 2 -or $gpu.modes[0].nvidiaUsb -ne 1 -or $gpu.modes[0].nvidiaBarrel -ne 0) { throw 'NVIDIA source preferences did not round-trip.' }
'PASS: legacy profiles keep NVIDIA unchanged and per-source settings round-trip.'
$before=[IO.File]::ReadAllText($path)
$loaded.modes[0].pluggedIn.cpuMinimum=90
$rejected=$false
try{[PowerConfig]::Save($path,$loaded)}catch{$rejected=$true}
if(!$rejected -or [IO.File]::ReadAllText($path) -ne $before){throw 'Invalid CPU range was not rejected without changing the saved file.'}
'PASS: invalid CPU range rejected; saved file unchanged.'
$loaded=[PowerConfig]::Load($path);$loaded.modes[1].battery.screenBrightness=0
$rejected=$false;try{$loaded.Validate()}catch{$rejected=$true}
if(!$rejected){throw 'Out-of-range brightness was accepted.'}
'PASS: invalid brightness rejected.'
$loaded=[PowerConfig]::Load($path);$loaded.modes[2].keyboardBrightness=4
$rejected=$false;try{$loaded.Validate()}catch{$rejected=$true}
if(!$rejected){throw 'Unsupported keyboard level was accepted.'}
'PASS: unsupported keyboard level rejected.'
$bad=Join-Path $folder 'invalid-config.json';[IO.File]::WriteAllText($bad,'{"version":1,"modes":[]}')
$rejected=$false;try{[PowerConfig]::Load($bad)}catch{$rejected=$true}
if(!$rejected -or [IO.File]::ReadAllText($bad) -ne '{"version":1,"modes":[]}'){throw 'Malformed file was not preserved.'}
'PASS: malformed configuration rejected and preserved.'
