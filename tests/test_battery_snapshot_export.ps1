# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 内存装配与隔离目录测试；不写生产注册表、不查询物理设备。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe'))) | Out-Null
Add-Type -AssemblyName System.Web.Extensions
$serializer=New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:checks=0
function Check($ok,$message){if(-not $ok){throw $message};$script:checks++}
$list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
$one=New-Object RazerBatteryTray.MouseBatteryInfo
$one.DeviceId='private-device-path';$one.DeviceName="雷蛇`"Cobra`nPro";$one.Brand='Razer'
$one.BatteryPercent=0;$one.IsConnected=$true;$one.IsPrimary=$true;$one.LastUpdated=[datetime]'2026-11-02T16:00:00Z'
$list.Add($one)
$two=New-Object RazerBatteryTray.MouseBatteryInfo
$two.DeviceId='sleeping';$two.DeviceName='AE7';$two.BatteryPercent=100;$two.IsSleeping=$true;$two.IsCharging=$true
$two.LastUpdated=[datetime]'2026-11-02T15:00:00Z';$list.Add($two)
$three=New-Object RazerBatteryTray.MouseBatteryInfo
$three.DeviceId='unknown';$three.DeviceName='PD200W';$three.BatteryPercent=-1;$list.Add($three)
$json=[RazerBatteryTray.BatterySnapshotExporter]::BuildJson($list,[datetime]'2026-11-02T16:30:00Z',1234)
$data=$serializer.DeserializeObject($json)
Check ($data.code -eq 200 -and $data.producerPid -eq 1234) 'Envelope and publisher identity'
Check ($data.data.schemaVersion -eq 1 -and $data.data.devices.Count -eq 3) 'Schema and complete device list'
Check ($data.data.devices[0].battery -eq 0) 'Zero percent remains valid'
Check ($data.data.devices[0].name -eq $one.DeviceName) 'Unicode quotes and control characters round-trip'
Check (-not $json.Contains('private-device-path')) 'Raw hardware identity is not exported'
Check ($data.data.devices[0].id.Length -eq 16) 'Stable hashed identity'
Check ($data.data.devices[1].battery -eq 100 -and $data.data.devices[1].isSleeping) 'Sleeping device retains cached reading'
Check (-not $data.data.devices[1].isCharging -and $data.data.devices[1].status -eq 'offline') 'Offline cache cannot claim live charging'
Check ($data.data.devices[1].batteryLastUpdatedUtc -match '^2026-11-02T15:00:00') 'Historical battery timestamp preserved'
Check ($null -eq $data.data.devices[2].battery) 'Unknown is null, never fake zero'
Check ($null -eq $data.data.devices[2].batteryLastUpdatedUtc) 'Unknown timestamp is null'
$empty=$serializer.DeserializeObject([RazerBatteryTray.BatterySnapshotExporter]::BuildJson($null,[datetime]::UtcNow,1234))
Check ($empty.data.devices.Count -eq 0) 'Empty source is a valid empty snapshot'
$tempDir=Join-Path ([IO.Path]::GetTempPath()) ('FerrisPulseSnapshotTest-'+[guid]::NewGuid().ToString('N'))
try {
    $file=Join-Path $tempDir 'battery.json'
    [RazerBatteryTray.BatterySnapshotExporter]::WriteSnapshot($file,$json)
    [RazerBatteryTray.BatterySnapshotExporter]::WriteSnapshot($file,'{"replacement":true}')
    Check ([IO.File]::ReadAllText($file) -eq '{"replacement":true}') 'Existing snapshot atomically replaced'
    Check (@(Get-ChildItem -LiteralPath $tempDir).Count -eq 1) 'No temporary file leaked'
} finally {
    if(Test-Path -LiteralPath (Join-Path $tempDir 'battery.json')){[IO.File]::Delete((Join-Path $tempDir 'battery.json'))}
    if(Test-Path -LiteralPath $tempDir){[IO.Directory]::Delete($tempDir)}
}
Write-Host "PASS: $script:checks battery snapshot export checks"
