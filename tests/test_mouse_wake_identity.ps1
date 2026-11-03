# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 精确鼠标身份回归：不创建窗口、不查询硬件、不写注册表。
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))|Out-Null
$method=[RazerBatteryTray.MainForm].GetMethod('IsTargetMouseHardwarePath',[Reflection.BindingFlags]'Static,NonPublic')
$razer=New-Object RazerBatteryTray.MouseBatteryInfo
$razer.DeviceId='Razer:1532:00B0';$razer.Brand='Razer';$razer.Category=[RazerBatteryTray.DeviceCategory]::Mouse
$ae7=New-Object RazerBatteryTray.MouseBatteryInfo
$ae7.DeviceId='Dareu:260D:1257:receiver';$ae7.Brand='Dareu';$ae7.Category=[RazerBatteryTray.DeviceCategory]::Mouse
$razerPath='\\?\HID#VID_1532&PID_00B0&MI_01#fixture'
$ae7Path='\\?\HID#VID_260D&PID_1257&MI_00#fixture'
$script:checks=0
function Check($ok,$name){if(-not $ok){throw $name};$script:checks++}
Check (-not $method.Invoke($null,@($ae7Path,$razer.PSObject.BaseObject))) 'AE7 input must never wake the primary Razer mouse'
Check (-not $method.Invoke($null,@($razerPath,$ae7.PSObject.BaseObject))) 'Razer input must never wake AE7'
Check ($method.Invoke($null,@($razerPath,$razer.PSObject.BaseObject))) 'Razer own input matches'
Check ($method.Invoke($null,@($ae7Path,$ae7.PSObject.BaseObject))) 'AE7 own input matches'
foreach($wrong in @('\\?\HID#VID_1532&PID_9999#fixture','VHF\HID#VID_1532&PID_00B0','ROOT\HID#VID_1532&PID_00B0','ACPI\VID_1532&PID_00B0')){
 Check (-not $method.Invoke($null,@($wrong,$razer.PSObject.BaseObject))) 'Other products and virtual sources do not match a real mouse'
}
$razer.DeviceId='Razer:1532:00BE';$razer.HardwareId='HID:1532:00BF'
Check ($method.Invoke($null,@('\\?\HID#VID_1532&PID_00BF#fixture',$razer.PSObject.BaseObject))) 'Normalized model identity still matches the actual wireless interface'
Check (-not $method.Invoke($null,@('\\?\HID#VID_1532&PID_00BE#fixture',$razer.PSObject.BaseObject))) 'Another interface is not accepted merely because its normalized model matches'
Write-Host "PASS: $script:checks mouse identity checks"
