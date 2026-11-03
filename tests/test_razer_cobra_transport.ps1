# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 接口身份纯逻辑，不发送任何 HID 命令。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))|Out-Null
$checks=0
foreach($battery in @(0,50,100)){
 foreach($charging in @($false,$true)){
  if(-not [RazerBatteryTray.RazerDeviceHelper]::DetermineIsDongle(0x00B0,'Razer Cobra Pro',$battery,$charging)){throw 'Wireless Cobra must stay wireless during standby or charging'}
  if([RazerBatteryTray.RazerDeviceHelper]::DetermineIsDongle(0x00AF,'Razer Cobra Pro',$battery,$charging)){throw 'Wired Cobra must stay wired even when a battery reading is available'}
  $checks+=2
 }
}
$state=New-Object RazerBatteryTray.RazerDeviceHardwareState
$state.DeviceId='Razer:1532:00B0';$state.BatteryPercent=100;$state.LastUpdated=[DateTime]::Now.AddHours(-1);$state.ConsecutiveFailures=1
$fallback=[RazerBatteryTray.RazerDeviceHelper]::CreateFallbackDeviceInfo($state.DeviceId,0x00B0,'Razer Cobra Pro',$true,$state)
if(-not $fallback.IsSleeping -or $fallback.Transport -ne '2.4G 无线' -or $fallback.LastUpdated -ne $state.LastUpdated){throw 'Cold cache without a successful session reply must not appear as fresh awake data'}
$checks++
Write-Host "PASS: $checks Cobra transport checks"
