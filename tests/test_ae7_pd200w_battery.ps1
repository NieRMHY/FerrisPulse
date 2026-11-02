# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# NieRMHY 真机回包回放；无物理 I/O、配置写入或生产注册表修改。
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe'))) | Out-Null
$script:checks=0
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Hex([string]$s, [int]$length=0) {
    [byte[]]$bytes = @($s.Split(' ') | ForEach-Object { [Convert]::ToByte($_,16) })
    if ($length -gt $bytes.Length) { [Array]::Resize([ref]$bytes,$length) }
    return ,$bytes
}
function Fix-AeChecksum([byte[]]$frame) {
    $sum=0; for($i=0;$i -lt 16;$i++){ $sum+=$frame[$i] }
    $frame[16]=[byte]((0x55-$sum) -band 255)
}
function Fix-MaonoChecksum([byte[]]$frame) {
    $len=[int]$frame[1]; $sum=0
    for($i=0;$i -lt $len-2;$i++){ $sum+=$frame[$i] }
    $checksum=(-$sum) -band 65535
    $frame[$len-2]=[byte]($checksum -band 255); $frame[$len-1]=[byte]($checksum -shr 8)
}
$p=0; $c=$false; $value=0
[byte[]]$ae=Hex '08 04 00 00 00 02 64 00 10 60 00 00 00 00 00 00 73'
Check ([RazerBatteryTray.DareuAe7Driver]::TryParseBattery($ae,[ref]$p,[ref]$c) -and $p -eq 100 -and -not $c) 'AE7 captured 100%'
Check (([BitConverter]::ToString([RazerBatteryTray.DareuAe7Driver]::BuildBatteryQuery())) -eq '08-04-00-00-00-00-00-00-00-00-00-00-00-00-00-00-49') 'AE7 official read query'
foreach($case in @(@(0,0),@(37,1),@(100,2))) {
    $frame=$ae.Clone();$frame[6]=$case[0];$frame[7]=$case[1];Fix-AeChecksum $frame
    Check ([RazerBatteryTray.DareuAe7Driver]::TryParseBattery($frame,[ref]$p,[ref]$c) -and $p -eq $case[0] -and $c -eq ($case[1] -gt 0)) 'AE7 synthetic battery/charge boundary'
}
foreach($index in @(0,1,2,3,4,5,6,7,16)) {
    $frame=$ae.Clone();$frame[$index]=255
    Check (-not [RazerBatteryTray.DareuAe7Driver]::TryParseBattery($frame,[ref]$p,[ref]$c)) "AE7 reject corrupted byte $index"
}
Check (-not [RazerBatteryTray.DareuAe7Driver]::TryParseBattery($ae[0..15],[ref]$p,[ref]$c)) 'AE7 short report rejected'
$frame=$ae.Clone();$frame[6]=101;Fix-AeChecksum $frame
Check (-not [RazerBatteryTray.DareuAe7Driver]::TryParseBattery($frame,[ref]$p,[ref]$c)) 'AE7 valid checksum cannot admit percentage above 100'
[byte[]]$ma=Hex 'C4 0B 00 00 04 42 00 3D 00 AE FE' 64
Check ([RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($ma,0x42,[ref]$value) -and $value -eq 61) 'PD200W captured TX1 61% TX2 0'
foreach($address in @(0x41,0x42,0x49)) {
    $query=[RazerBatteryTray.MaonoPd200wDriver]::BuildReadQuery($address)
    Check ($query.Length -eq 64 -and $query[0] -eq 0xC4 -and $query[4] -eq 4 -and $query[5] -eq $address) 'Maono bounded read query'
}
Check (([BitConverter]::ToString([RazerBatteryTray.MaonoPd200wDriver]::BuildReadQuery(0x42)[0..8])) -eq 'C4-09-00-00-04-42-00-ED-FE') 'Maono captured query bytes'
$threw=$false;try{[RazerBatteryTray.MaonoPd200wDriver]::BuildReadQuery(0x2022)|Out-Null}catch{$threw=$true}
Check $threw 'Maono arbitrary register queries prohibited'
foreach($index in @(0,1,2,3,4,5,6,7,8,9,10)) {
    $frame=$ma.Clone();$frame[$index]=255
    Check (-not [RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($frame,0x42,[ref]$value)) "Maono reject corrupted byte $index"
}
Check (-not [RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($ma[0..10],0x42,[ref]$value)) 'Maono short report rejected'
$frame=$ma.Clone();$frame[4]=3;Fix-MaonoChecksum $frame
Check (-not [RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($frame,0x42,[ref]$value)) 'Maono unsolicited write notification rejected'
$frame=$ma.Clone();$frame[7]=0;$frame[8]=100;Fix-MaonoChecksum $frame
Check ([RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($frame,0x42,[ref]$value) -and $value -eq 25600) 'Maono separate channel values including zero'
$frame=$ma.Clone();$frame[8]=101;Fix-MaonoChecksum $frame
Check (-not [RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($frame,0x42,[ref]$value)) 'Maono valid checksum cannot admit invalid second channel percentage'
$frame=$ma.Clone();$frame[5]=0x41;Fix-MaonoChecksum $frame
Check (-not [RazerBatteryTray.MaonoPd200wDriver]::TryParseRegister($frame,0x42,[ref]$value)) 'Maono valid checksum cannot admit wrong register'
foreach($spec in @(@('DareuAe7Driver',0x260D,0x1257,0xFF02,2,17),@('MaonoPd200wDriver',0x352F,0x040E,12,1,64))) {
    $d=New-Object RazerBatteryTray.HidDeviceDescriptor
    $d.VendorId=$spec[1];$d.ProductId=$spec[2];$d.UsagePage=$spec[3];$d.Usage=$spec[4];$d.InputReportLength=$spec[5];$d.OutputReportLength=$spec[5]
    $type=('RazerBatteryTray.'+$spec[0]) -as [type]; $method=$type.GetMethod('MatchesEndpoint')
    Check ($method.Invoke($null,@($d.PSObject.BaseObject))) 'Exact endpoint accepted'
    foreach($field in @('VendorId','ProductId','UsagePage','Usage','InputReportLength','OutputReportLength','FeatureReportLength')) {
        $before=$d.$field;$d.$field=$before+1
        Check (-not $method.Invoke($null,@($d.PSObject.BaseObject))) "Reject wrong endpoint $field"
        $d.$field=$before
    }
    $driver=[Activator]::CreateInstance($type);$dev=New-Object RazerBatteryTray.MouseBatteryInfo
    $dev.HardwareId=('hid#vid_{0:x4}&pid_{1:x4}' -f $d.VendorId,$d.ProductId)
    $driver.PopulateCapabilities($dev)
    Check ($dev.Capabilities.SupportsBattery -and -not $dev.Capabilities.SupportsDpi -and -not $dev.Capabilities.SupportsPollingRate -and -not $dev.Capabilities.SupportsHardwareDpiMonitor) 'Read-only capabilities'
    Check (([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev)).DriverId -eq $driver.DriverId) 'Built-in registry routes exact PID'
}
Check (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x260D,'hid#vid_260d&pid_1257','DAREU Receiver')) 'AE7 isolated from generic protocol probes'
Check ([int][RazerBatteryTray.DeviceCategory]::Dongle -eq 6 -and [int][RazerBatteryTray.DeviceCategory]::Microphone -eq 7) 'Existing category persistence values preserved'
Write-Host "PASS: $script:checks AE7/PD200W replay and routing checks"
