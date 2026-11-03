# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 实机 GPA6 回复回放与端点隔离；不打开物理设备、不写注册表。
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))|Out-Null
$script:checks=0
function Check($ok,$message){if(-not $ok){throw $message};$script:checks++}
Check ([RazerBatteryTray.FlydigiDeviceHelper]::GetModelName(0x2502,150,'Flydigi APEX 6') -eq 'Flydigi APEX 6') 'Real USB product name takes precedence over ambiguous device mode/type codes'
Check ([RazerBatteryTray.FlydigiDeviceHelper]::GetTransportName(1) -ne '有线 USB') 'Wireless receiver mode 1 cannot claim a wired connection'
$bytes=[byte[]]('00-5A-A5-01-01-00-96-01-00-00-00-00-0A-45-01-00-90-55-05-09-01-11-00-00-01-43-00-00-10-61-9F-00-42'.Split('-')|ForEach-Object{[Convert]::ToByte($_,16)})
$sum=0;for($i=3;$i -lt 32;$i++){$sum+=$bytes[$i]}
Check (($sum -band 255) -eq $bytes[32]) 'Captured response checksum matches the on-device frame'
$battery=0;$charging=$false;$model=0;$mode=0
$ok=[RazerBatteryTray.FlydigiDeviceHelper]::ParseInfo($bytes,$bytes.Length,[ref]$battery,[ref]$charging,[ref]$model,[ref]$mode)
Check ($ok -and $battery -eq 100 -and -not $charging -and $mode -eq 1) 'Real 2.4G reply reports full battery without charging'
$descriptor=New-Object RazerBatteryTray.HidDeviceDescriptor
$descriptor.VendorId=0x37D7;$descriptor.ProductId=0x2502;$descriptor.UsagePage=0xFFA0;$descriptor.Usage=1
$descriptor.InputReportLength=33;$descriptor.OutputReportLength=33;$descriptor.FeatureReportLength=0
Check ([RazerBatteryTray.FlydigiDeviceHelper]::IsBatteryEndpoint($descriptor)) 'Exact GPA6 receiver endpoint accepted'
foreach($property in 'Usage','UsagePage','InputReportLength','OutputReportLength','FeatureReportLength'){
    $previous=$descriptor.$property;$descriptor.$property=$previous+1
    Check (-not [RazerBatteryTray.FlydigiDeviceHelper]::IsBatteryEndpoint($descriptor)) ('Other endpoint rejected: '+$property)
    $descriptor.$property=$previous
}
Write-Host "PASS: $script:checks APEX 6 receiver checks"
