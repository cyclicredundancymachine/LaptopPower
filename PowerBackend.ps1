param(
    [ValidateSet('Apply','Restore','Verify','Check')][string]$Action='Verify',
    [ValidateRange(-1,2)][int]$ModeIndex=-1,
    [string]$ConfigPath=(Join-Path $PSScriptRoot 'config.json'),
    [switch]$TestProfiles
)
$ErrorActionPreference='Stop'
trap { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $PSScriptRoot 'Configuration.cs') -ReferencedAssemblies System.Web.Extensions
Add-Type -Path (Join-Path $PSScriptRoot 'AsusControl.cs')
$config=[PowerConfig]::Load($ConfigPath)
$stateFile=Join-Path $PSScriptRoot 'restore.json'
$guids=@('b86b6310-905f-4af3-88c8-cb65129d0d01','b86b6310-905f-4af3-88c8-cb65129d0d02','b86b6310-905f-4af3-88c8-cb65129d0d03')
$script:rows=New-Object 'System.Collections.Generic.List[object]'
$script:writeErrors=@{}
$script:asus=$null
if (!$TestProfiles) {try {$script:asus=New-Object LaptopAsus} catch {}}
$definitions=@(
    @{key='cpuMinimum';name='CPU minimum';group='SUB_PROCESSOR';setting='PROCTHROTTLEMIN';unit='percent'},
    @{key='cpuMaximum';name='CPU maximum';group='SUB_PROCESSOR';setting='PROCTHROTTLEMAX';unit='percent'},
    @{key='boostMode';name='CPU boost';group='SUB_PROCESSOR';setting='be337238-0d82-4146-a960-4f3749d470c7';unit='boost'},
    @{key='efficiencyPreference';name='CPU efficiency preference';group='SUB_PROCESSOR';setting='36687f9e-e3a5-4dbf-b1dc-15eb381c6863';unit='percent'},
    @{key='screenBrightness';name='Screen brightness plan';group='SUB_VIDEO';setting='VIDEONORMALLEVEL';unit='percent'},
    @{key='displayTimeoutMinutes';name='Turn screen off after';group='SUB_VIDEO';setting='VIDEOIDLE';unit='seconds'},
    @{key='sleepTimeoutMinutes';name='Sleep after';group='SUB_SLEEP';setting='STANDBYIDLE';unit='seconds'}
)
function Add-Row($setting,$requested,$actual,$status,$detail='') {
    $script:rows.Add([pscustomobject]@{setting=[string]$setting;requested=[string]$requested;actual=[string]$actual;status=[string]$status;detail=[string]$detail})
}
function Power([string[]]$Arguments) {
    $result=& "$env:SystemRoot\System32\powercfg.exe" @Arguments 2>&1
    if($LASTEXITCODE -ne 0) {throw ($result -join ' ')}
    return ($result -join "`r`n")
}
function Active-Plan {
    $text=Power @('/getactivescheme')
    if($text -notmatch '[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}') {throw 'Cannot read the active Windows power plan.'}
    return $Matches[0].ToLowerInvariant()
}
function Read-Values($guid,$definition) {
    $text=Power @('/qh',$guid,$definition.group,$definition.setting)
    $values=@([regex]::Matches($text,'0x[0-9a-fA-F]{8}') | ForEach-Object {[Convert]::ToInt32($_.Value.Substring(2),16)})
    if($values.Count -lt 2) {throw 'Windows does not expose this setting.'}
    return @($values[-2],$values[-1])
}
function Display-Value([int]$value,$unit) {
    switch($unit) {
        'percent' {return "$value%"}
        'seconds' {if($value -eq 0){return 'Never'}; return ('{0:g} min' -f ($value/60.0))}
        'boost' {return @('Disabled','Enabled','Aggressive','Efficient enabled','Efficient aggressive','Aggressive at guaranteed','Efficient aggressive at guaranteed')[$value]}
        default {return [string]$value}
    }
}
function Read-Screens {
    return @(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness | ForEach-Object {[pscustomobject]@{Instance=$_.InstanceName;Brightness=[int]$_.CurrentBrightness}})
}
function Write-Screens($screens) {
    $methods=@(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods)
    if(!$screens.Count -or !$methods.Count) {throw 'No controllable internal screen was found.'}
    foreach($screen in $screens) {
        $targets=@($methods | Where-Object InstanceName -eq $screen.Instance)
        if(!$targets.Count) {throw 'The saved internal screen is unavailable.'}
        foreach($target in $targets) {
            $null=Invoke-CimMethod -InputObject $target -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]1;Brightness=[byte]$screen.Brightness}
        }
    }
    for($attempt=0;$attempt -lt 5;$attempt++) {
        $actual=@(Read-Screens);$matches=$true
        foreach($screen in $screens) {
            $found=@($actual | Where-Object Instance -eq $screen.Instance)
            if(!$found.Count -or [int]$found[0].Brightness -ne [int]$screen.Brightness) {$matches=$false}
        }
        if($matches){return}; Start-Sleep -Milliseconds 250
    }
    throw 'Screen readback did not match the requested brightness.'
}
function Read-Asus([uint32]$id) {if(!$script:asus){return -1};try{return $script:asus.Read($id)}catch{return -1}}
function Load-State {
    if(!(Test-Path -LiteralPath $stateFile)){return $null}
    $state=Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    if($state.Plan -notmatch '^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$'){throw 'The restore record has an invalid power plan.'}
    return $state
}
function Save-Original {
    $state=Load-State; if($state){return $state}
    $plan=Active-Plan
    if($guids -contains $plan){throw 'The original restore record is missing. Select a normal Windows power plan before applying a mode.'}
    $screens=@();try{$screens=@(Read-Screens)}catch{}
    $state=[pscustomobject]@{Plan=$plan;Screens=$screens}
    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$stateFile.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$stateFile.tmp" -Destination $stateFile -Force
    return $state
}
function Desired-Value($values,$definition,$state,$currentScreens) {
    $key=$definition.key
    if($key -eq 'screenBrightness') {
        if($values.screenAction -eq 'Set'){return [int]$values.screenBrightness}
        if($values.screenAction -eq 'Restore' -and $state -and @($state.Screens).Count){return [int]$state.Screens[0].Brightness}
        if($values.screenAction -eq 'Keep' -and $currentScreens.Count){return [int]$currentScreens[0].Brightness}
        return $null
    }
    if($definition.unit -eq 'seconds') {if($values.$key -lt 0){return $null};return ([int]$values.$key*60)}
    return [int]$values.$key
}
function Prepare-Plan($guid,$mode,$state,$currentScreens) {
    if((Power @('/list')) -notmatch [regex]::Escape($guid)) {
        $null=Power @('/duplicatescheme','381b4222-f694-41f0-9685-ff5bb260df2e',$guid)
    }
    $null=Power @('/changename',$guid,"Laptop Power - $($mode.name)")
    $previous=Active-Plan
    for($s=0;$s -lt 2;$s++) {
        $values=@($mode.pluggedIn,$mode.battery)[$s]
        foreach($definition in $definitions) {
            $key="$s/$($definition.key)"
            try {
                $desired=Desired-Value $values $definition $state $currentScreens
                if($null -eq $desired){$desired=(Read-Values $previous $definition)[$s]}
                $script:resolved[$key]=[int]$desired
                $verb=@('/setacvalueindex','/setdcvalueindex')[$s]
                $null=Power @($verb,$guid,$definition.group,$definition.setting,[string]$desired)
            }catch{$script:writeErrors[$key]=$_.Exception.Message}
        }
    }
}
function Add-ProfileRows($guid,$mode,$state,$screens,[bool]$active) {
    foreach($definition in $definitions) {
        $actual=$null;$readError=$null
        try{$actual=Read-Values $guid $definition}catch{$readError=$_.Exception.Message}
        for($s=0;$s -lt 2;$s++) {
            $label=$definition.name+' - '+@('plugged in','battery')[$s]
            $key="$s/$($definition.key)";$desired=$null
            if($mode){
                $values=@($mode.pluggedIn,$mode.battery)[$s]
                $desired=Desired-Value $values $definition $state $screens
                if($definition.key -eq 'screenBrightness' -and $values.screenAction -eq 'Keep' -and !$script:resolved.ContainsKey($key)){$desired=$null}
            }
            if($script:resolved.ContainsKey($key)){$desired=$script:resolved[$key]}
            $request=if($null -eq $desired){'Keep / observe'}else{Display-Value $desired $definition.unit}
            if($readError){Add-Row $label $request 'Unavailable' 'Unsupported' $readError;continue}
            $valueText=Display-Value $actual[$s] $definition.unit
            if($script:writeErrors.ContainsKey($key)){Add-Row $label $request $valueText 'Failed' $script:writeErrors[$key]}
            elseif($null -eq $desired){Add-Row $label $request $valueText 'Observed' 'No change requested.'}
            elseif($actual[$s] -ne $desired){Add-Row $label $request $valueText 'Mismatch' 'Windows value differs from the requested configuration.'}
            elseif(!$active){Add-Row $label $request $valueText 'Saved only' 'This power plan is not active.'}
            else{Add-Row $label $request $valueText 'Verified' 'Read back from Windows power settings. This is a policy value, not measured CPU speed or power draw.'}
        }
    }
}
function Add-ScreenRow($mode,$state,$before,[bool]$apply) {
    $s=if([Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus -eq 'Online'){0}else{1}
    $desired=$null;$request='Observe current brightness'
    if($mode){
        $values=@($mode.pluggedIn,$mode.battery)[$s]
        $desired=Desired-Value $values $definitions[4] $state $before
        if(!$apply -and $values.screenAction -eq 'Keep'){$desired=$null}
        $request=if($null -eq $desired){$values.screenAction}else{"$desired% ($($values.screenAction))"}
    }
    try {
        if($apply -and $null -ne $desired){
            $targets=@($before | ForEach-Object {[pscustomobject]@{Instance=$_.Instance;Brightness=$desired}})
            Write-Screens -screens $targets
        }
        $screens=@(Read-Screens)
        if(!$screens.Count){throw 'No controllable internal screen was found.'}
        $text=($screens | ForEach-Object {"$($_.Brightness)%"}) -join ', '
        if($null -eq $desired){Add-Row 'Screen brightness - live' $request $text 'Observed'}
        elseif(@($screens | Where-Object Brightness -ne $desired).Count){Add-Row 'Screen brightness - live' $request $text 'Mismatch'}
        else{Add-Row 'Screen brightness - live' $request $text 'Verified' 'Read back from the internal display.'}
    }catch{Add-Row 'Screen brightness - live' $request 'Unavailable' 'Failed' $_.Exception.Message}
}
function Output-Report($mode,$context) {
    $counts=@($script:rows | Group-Object status | ForEach-Object {"$($_.Count) $($_.Name.ToLowerInvariant())"}) -join ', '
    $report=[pscustomobject]@{mode=$mode;time=[DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz');summary="$context $counts.";targetModeIndex=$ModeIndex;rows=@($script:rows.ToArray())}
    $json=$report | ConvertTo-Json -Depth 8
    $json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'last-report.json') -Encoding UTF8
    $json
}
$script:resolved=@{}
try {
    if($TestProfiles){
        $original=Active-Plan;$created=New-Object 'System.Collections.Generic.List[string]'
        try {
            foreach($mode in $config.modes){
                $guid=[guid]::NewGuid().ToString();$created.Add($guid)
                $script:writeErrors=@{};$script:resolved=@{}
                Prepare-Plan $guid $mode $null @()
                if($script:writeErrors.Count){throw ($script:writeErrors.Values -join '; ')}
                foreach($definition in $definitions){
                    $actual=Read-Values $guid $definition
                    for($s=0;$s -lt 2;$s++){if($actual[$s] -ne $script:resolved["$s/$($definition.key)"]){throw "Readback failed: $($mode.name), $($definition.key), source $s"}}
                }
                "PASS: $($mode.name), all seven Windows settings on mains and battery."
            }
            if((Active-Plan) -ne $original){throw 'Active plan changed during preparation.'}
            'PASS: active power plan unchanged.'
        }finally{foreach($guid in $created){if((Power @('/list')) -match [regex]::Escape($guid)){$null=Power @('/delete',$guid)}}}
        exit 0
    }
    $screens=@();try{$screens=@(Read-Screens)}catch{}
    if($Action -eq 'Restore'){
        $state=Load-State
        if(!$state){Add-Row 'Restore' 'Saved original settings' 'No restore record' 'Skipped';Output-Report 'Restore' 'No settings changed.';exit 0}
        try {
            $null=Power @('/setactive',$state.Plan)
            $actual=Active-Plan
            Add-Row 'Active power plan' $state.Plan $actual $(if($actual -eq $state.Plan){'Verified'}else{'Mismatch'})
        }catch{Add-Row 'Active power plan' $state.Plan 'Unavailable' 'Failed' $_.Exception.Message}
        try {
            if(!@($state.Screens).Count){throw 'Original screen brightness was not readable.'}
            Write-Screens -screens @($state.Screens)
            Add-Row 'Screen brightness - live' (($state.Screens.Brightness | ForEach-Object {"$_%"}) -join ', ') ((@(Read-Screens).Brightness | ForEach-Object {"$_%"}) -join ', ') 'Verified'
        }catch{Add-Row 'Screen brightness - live' 'Original brightness' 'Unavailable' 'Failed' $_.Exception.Message}
        if(!@($script:rows | Where-Object {$_.status -in @('Failed','Mismatch')}).Count){Remove-Item -LiteralPath $stateFile}
        Output-Report 'Restore' 'Original settings restore results.';exit 0
    }
    $active=Active-Plan
    if($Action -in @('Verify','Check') -and $ModeIndex -lt 0){$ModeIndex=[Array]::IndexOf($guids,$active)}
    $mode=if($ModeIndex -ge 0){$config.modes[$ModeIndex]}else{$null}
    $guid=if($ModeIndex -ge 0){$guids[$ModeIndex]}else{$active}
    $state=Load-State
    if($Action -eq 'Apply'){
        if(!$mode){throw 'Select a mode before applying settings.'}
        $state=Save-Original
        Prepare-Plan $guid $mode $state $screens
        $critical=@($script:writeErrors.Keys | Where-Object {$_ -match 'cpuMinimum|cpuMaximum'})
        if(!$critical.Count){
            try{$null=Power @('/setactive',$guid)}catch{$script:writeErrors['activate']=$_.Exception.Message}
        }
        $active=Active-Plan
        if($active -eq $guid -and !$critical.Count){Add-ScreenRow $mode $state $screens $true}
        else{Add-Row 'Screen brightness - live' 'Mode setting' 'Not applied' 'Skipped'}
    }
    $isActive=$active -eq $guid
    Add-Row 'Active power plan' $(if($mode){$mode.name}else{'Current Windows plan'}) $active $(if($isActive){'Verified'}else{'Mismatch'}) $(if(!$isActive){'The requested mode is not active.'}else{'Windows confirms this plan is active.'})
    # Verification reads the ACTIVE plan, even if another app switched away from the target.
    $readGuid=if($Action -eq 'Apply'){$guid}else{$active}
    $canVerify=if($Action -eq 'Apply'){$isActive}else{$true}
    Add-ProfileRows $readGuid $mode $state $screens $canVerify
    if($Action -ne 'Apply'){Add-ScreenRow $mode $state $screens $false}
    Output-Report $(if($mode){$mode.name}else{'Current Windows settings'}) $(if($Action -eq 'Apply'){'Apply results.'}else{'Read-only check against the current configuration.'})
}finally{if($script:asus){$script:asus.Dispose()}}
