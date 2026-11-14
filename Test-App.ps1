$ErrorActionPreference='Stop'
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('LaptopPowerChecks-'+[guid]::NewGuid()+'.exe')
$sources=@('NativeApp.cs','PowerWindow.cs','PowerWatch.cs','GpuSwitch.cs','TrayClicks.cs','AsusControl.cs','SettingsWindow.cs','Configuration.cs','Test-App.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /main:AppChecks "/out:$temporary" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sources
if($LASTEXITCODE -ne 0){throw 'App checks did not compile.'}
& $temporary
if($LASTEXITCODE -ne 0){throw 'App checks failed.'}
