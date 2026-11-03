# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 必须在测试 HKCU 沙箱运行；不查询硬件，缓存时间不可被恢复操作改写。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))|Out-Null
$script:checks=0
function Check($ok,$name){if(-not $ok){throw $name};$script:checks++}
$id='Flydigi:37D7:2502';$observed=[DateTime]::UtcNow.AddHours(-1)
[RazerBatteryTray.DriverStateStore]::SaveBatteryObservation($id,100,$true,'Flydigi APEX 6',$observed)
$offline=[RazerBatteryTray.FlydigiDeviceHelper]::CreateOfflineDevice(0x2502,$null)
Check ($null -ne $offline -and $offline.DeviceId -eq $id) 'Known gamepad remains after its HID endpoint disappears'
Check (-not $offline.IsConnected -and -not $offline.IsDonglePresent) 'Absent endpoint is not misreported as a detected receiver'
Check ($offline.BatteryPercent -eq 100 -and $offline.LastUpdated.ToUniversalTime() -eq $observed) 'Historical percent and original observation time survive restart'
Check (-not $offline.IsCharging -and $offline.IsChargingUnknown) 'Offline card cannot reuse a historical charging animation'
Check ($offline.Category -eq [RazerBatteryTray.DeviceCategory]::Gamepad) 'Offline card keeps its gamepad icon'
Check ($null -eq [RazerBatteryTray.FlydigiDeviceHelper]::CreateOfflineDevice(0x2401,$null)) 'An unseen supported model is not invented'
Check ($null -eq [RazerBatteryTray.FlydigiDeviceHelper]::CreateOfflineDevice(0x9999,$null)) 'Other products are never restored'
$descriptor=New-Object RazerBatteryTray.HidDeviceDescriptor
$descriptor.VendorId=0x37D7;$descriptor.ProductId=0x2502;$descriptor.UsagePage=0xFFA0;$descriptor.Usage=1
$descriptor.InputReportLength=33;$descriptor.OutputReportLength=33;$descriptor.ProductString='Flydigi APEX 6'
$present=[RazerBatteryTray.FlydigiDeviceHelper]::CreateOfflineDevice(0x2502,$descriptor)
Check ($present.IsDonglePresent -and -not $present.IsConnected -and $present.LastUpdated.ToUniversalTime() -eq $observed) 'Detected receiver with a closed handset remains an offline card'
$devices=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]';$devices.Add($offline)
$json=[RazerBatteryTray.BatterySnapshotExporter]::BuildJson($devices,[DateTime]::UtcNow,1234)
Check ($json.Contains('"status":"offline"') -and $json.Contains('"source":"Flydigi"')) 'Taskbar receives the retained gamepad as offline instead of a live cached percent'
Write-Host "PASS: $script:checks gamepad history checks"
