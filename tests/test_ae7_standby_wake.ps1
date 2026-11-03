# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 实机状态帧回放与多鼠标唤醒策略；不读写物理设备或生产注册表。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))|Out-Null
if(-not ('RazerBatteryTray.ReceiverBatteryWakePolicy' -as [type])){throw 'Receiver wake handling is not implemented'}
$script:checks=0
function Check($ok,$message){if(-not $ok){throw $message};$script:checks++}
function Bytes($hex){[byte[]]($hex.Split('-')|ForEach-Object{[Convert]::ToByte($_,16)})}
$awake=Bytes '08-03-00-00-00-01-01-03-FD-51-00-00-00-00-00-00-F7'
$idle=Bytes '08-03-00-00-00-01-00-03-FD-51-00-00-00-00-00-00-F8'
$active=$false
Check ([RazerBatteryTray.DareuAe7Driver]::TryParseActivity($awake,[ref]$active) -and $active) 'Captured awake state accepted'
Check ([RazerBatteryTray.DareuAe7Driver]::TryParseActivity($idle,[ref]$active) -and -not $active) 'Receiver explicitly reports standby while still answering'
Check ([BitConverter]::ToString([RazerBatteryTray.DareuAe7Driver]::BuildActivityQuery()) -eq '08-03-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4A') 'Only the official read-only activity command is sent'
foreach($index in @(0,1,2,3,4,5,6,16)){
 $bad=$awake.Clone();$bad[$index]=255
 Check (-not [RazerBatteryTray.DareuAe7Driver]::TryParseActivity($bad,[ref]$active)) ('Corrupt activity frame rejected: '+$index)
}
Check (-not [RazerBatteryTray.DareuAe7Driver]::TryParseActivity($awake[0..15],[ref]$active)) 'Short activity frame rejected'
$list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
$razer=New-Object RazerBatteryTray.MouseBatteryInfo;$razer.DeviceId='Razer:1532:00B0';$razer.Category=[RazerBatteryTray.DeviceCategory]::Mouse;$razer.IsConnected=$true;$razer.IsPrimary=$true;$list.Add($razer)
$ae7=New-Object RazerBatteryTray.MouseBatteryInfo;$ae7.DeviceId='Dareu:260D:1257:receiver';$ae7.Category=[RazerBatteryTray.DeviceCategory]::Mouse;$ae7.IsDonglePresent=$true;$ae7.IsSleeping=$true;$ae7.BatteryPercent=100
(New-Object RazerBatteryTray.DareuAe7Driver).PopulateCapabilities($ae7);$list.Add($ae7)
$path='\\?\HID#VID_260D&PID_1257&MI_00#fixture'
Check ([RazerBatteryTray.ReceiverBatteryWakePolicy]::HasPending($list)) 'A non-primary receiver can keep its wake listener armed'
Check ([object]::ReferenceEquals([RazerBatteryTray.ReceiverBatteryWakePolicy]::FindTarget($list,$path),$ae7)) 'AE7 input wakes AE7 even while Razer is primary'
foreach($wrong in @('\\?\HID#VID_1532&PID_00B0#fixture','\\?\HID#VID_260D&PID_9999#fixture','VHF\HID#VID_260D&PID_1257','ROOT\HID#VID_260D&PID_1257')){
 Check ($null -eq [RazerBatteryTray.ReceiverBatteryWakePolicy]::FindTarget($list,$wrong)) 'Unrelated mouse and virtual input cannot trigger receiver polling'
}
Check ([RazerBatteryTray.ReceiverBatteryWakePolicy]::GetStatusText($ae7) -eq '接收器在线 · 待机') 'Standby is distinct from physical disconnection'
$ae7.IsSleeping=$false
Check ([RazerBatteryTray.ReceiverBatteryWakePolicy]::GetStatusText($ae7) -eq '接收器在线 · 等待电量') 'Timeout alone does not claim confirmed sleep'
$ae7.IsDonglePresent=$false
Check (-not [RazerBatteryTray.ReceiverBatteryWakePolicy]::HasPending($list)) 'An unplugged receiver does not retain the wake listener'
$ae7.IsDonglePresent=$true;$ae7.IsConnected=$true
Check (-not [RazerBatteryTray.ReceiverBatteryWakePolicy]::HasPending($list)) 'Awake mouse movement does not cause repeated battery queries'
$policy=New-Object RazerBatteryTray.ReceiverBatteryWakePolicy
$now=[DateTime]::UtcNow
Check ($policy.TrySchedule($now)) 'First wake refresh schedules'
Check (-not $policy.TrySchedule($now.AddSeconds(5))) 'Only one wake refresh may be queued'
$policy.Complete()
Check (-not $policy.TrySchedule($now.AddMilliseconds(500))) 'High-rate input is coalesced'
Check ($policy.TrySchedule($now.AddSeconds(3))) 'A later real wake can retry'
$policy.Complete()
Check ($script:checks -eq 26) 'All standby and wake scenarios executed'
Write-Host "PASS: $script:checks AE7 standby and wake checks"
