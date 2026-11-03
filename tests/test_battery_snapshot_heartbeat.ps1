# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 只使用临时目录与内存装配；不改用户配置、不查询物理设备。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe'))) | Out-Null
if(-not ('RazerBatteryTray.BatterySnapshotPublisher' -as [type])){throw 'Heartbeat publisher is not implemented'}
Add-Type -AssemblyName System.Web.Extensions
Add-Type 'public static class BatteryPublisherTestSwitch { public static volatile bool Enabled; public static bool Read() { return Enabled; } }'
$switchMethod=[BatteryPublisherTestSwitch].GetMethod('Read')
$enabled=[Delegate]::CreateDelegate([Func[bool]],$switchMethod)
$serializer=New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:checks=0
function Check($ok,$message){if(-not $ok){throw $message};$script:checks++}
function WaitFile($path,$exists){
    for($attempt=0;$attempt -lt 40;$attempt++){
        if([IO.File]::Exists($path) -eq $exists){return}
        Start-Sleep -Milliseconds 50
    }
    throw ('Timed out waiting for snapshot existence: '+$exists)
}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('FerrisPulseHeartbeatTest-'+[guid]::NewGuid().ToString('N'))
$path=Join-Path $folder 'battery.json'
$publisher=$null
$replacement=$null
try {
    $publisher=New-Object RazerBatteryTray.BatterySnapshotPublisher($path,$enabled,200)
    $publisher.Start()
    Start-Sleep -Milliseconds 250
    Check (-not [IO.File]::Exists($path)) 'Disabled publisher creates no snapshot'
    [BatteryPublisherTestSwitch]::Enabled=$true
    WaitFile $path $true
    $initial=$serializer.DeserializeObject([IO.File]::ReadAllText($path))
    Check ($initial.data.devices.Count -eq 0 -and $null -eq $initial.data.observedAtUtc) 'Cold heartbeat reports unknown until the first hardware observation'
    $list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $mouse=New-Object RazerBatteryTray.MouseBatteryInfo
    $mouse.DeviceId='fixture';$mouse.Brand='Razer';$mouse.BatteryPercent=51;$mouse.IsConnected=$true
    $list.Add($mouse)
    $publisher.Publish($list)
    $first=$serializer.DeserializeObject([IO.File]::ReadAllText($path))
    Check ($first.data.devices[0].battery -eq 51) 'Completed observation is published immediately'
    Check ($first.data.observedAtUtc -eq $first.data.generatedAtUtc) 'First publication records the observation time'
    $mouse.BatteryPercent=7
    Start-Sleep -Milliseconds 450
    $heartbeat=$serializer.DeserializeObject([IO.File]::ReadAllText($path))
    Check ($heartbeat.data.generatedAtUtc -ne $first.data.generatedAtUtc) 'Heartbeat continues without another hardware query'
    Check ($heartbeat.data.observedAtUtc -eq $first.data.observedAtUtc) 'Heartbeat never refreshes the age of hardware observations'
    Check ($heartbeat.data.devices[0].battery -eq 51) 'Heartbeat uses an immutable observation rather than mutable device objects'
    [IO.File]::Delete($path)
    WaitFile $path $true
    Check ($serializer.DeserializeObject([IO.File]::ReadAllText($path)).data.devices[0].battery -eq 51) 'Deleted snapshot is recreated automatically'
    [BatteryPublisherTestSwitch]::Enabled=$false
    WaitFile $path $false
    Check (-not [IO.File]::Exists($path)) 'Disabling export removes only the owned snapshot'
    [BatteryPublisherTestSwitch]::Enabled=$true
    WaitFile $path $true
    Check ($serializer.DeserializeObject([IO.File]::ReadAllText($path)).data.devices[0].battery -eq 51) 'Re-enabling export reconnects without restarting the application'
    $publisher.Dispose()
    WaitFile $path $false
    Start-Sleep -Milliseconds 400
    Check (-not [IO.File]::Exists($path)) 'Shutdown cannot race a timer into recreating stale data'
    $replacement=New-Object RazerBatteryTray.BatterySnapshotPublisher($path,$enabled,200)
    $replacement.Start();$replacement.Publish($list)
    WaitFile $path $true
    $next=$serializer.DeserializeObject([IO.File]::ReadAllText($path))
    Check ($next.producerSession -ne $first.producerSession -and $next.data.devices[0].battery -eq 7) 'Restart publishes a new session and fresh observations'
    $publisher.Dispose()
    Check ([IO.File]::Exists($path)) 'Old publisher disposal cannot remove a replacement session'
} finally {
    if($publisher){$publisher.Dispose()}
    if($replacement){$replacement.Dispose()}
    if([IO.File]::Exists($path)){[IO.File]::Delete($path)}
    if([IO.Directory]::Exists($folder)){[IO.Directory]::Delete($folder)}
}
Check ($script:checks -eq 13) 'All heartbeat scenarios completed'
Write-Host "PASS: $script:checks battery heartbeat checks"
