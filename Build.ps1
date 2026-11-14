$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'LaptopPower.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\App.manifest" "/resource:$PSScriptRoot\PowerBackend.ps1,PowerBackend.ps1" "/resource:$PSScriptRoot\AsusControl.cs,AsusControl.cs" "/resource:$PSScriptRoot\Configuration.cs,Configuration.cs" "$PSScriptRoot\NativeApp.cs" "$PSScriptRoot\PowerWindow.cs" "$PSScriptRoot\PowerWatch.cs" "$PSScriptRoot\TrayClicks.cs" "$PSScriptRoot\GpuSwitch.cs" "$PSScriptRoot\AsusControl.cs" "$PSScriptRoot\SettingsWindow.cs" "$PSScriptRoot\Configuration.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $output"
