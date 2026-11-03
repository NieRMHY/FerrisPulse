# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 假驱动记录调用，不进行任何物理 I/O。结果只可替换精确身份，保留无关设备状态。
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot '..\FerrisPulse.exe'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))|Out-Null
$provider=New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters=New-Object System.CodeDom.Compiler.CompilerParameters
$parameters.GenerateInMemory=$true
$parameters.ReferencedAssemblies.Add('System.dll')|Out-Null
$parameters.ReferencedAssemblies.Add('System.Drawing.dll')|Out-Null
$parameters.ReferencedAssemblies.Add((Resolve-Path $exe).Path)|Out-Null
$source=@'
using System;using System.Collections.Generic;using RazerBatteryTray;
public class BatteryRefreshSpy : HardwareDriverBase {
 public int Vid,Pid,Calls;public List<MouseBatteryInfo> Results=new List<MouseBatteryInfo>();
 public override string DriverId {get{return "UnitBatterySpy:"+Vid+":"+Pid;}}
 public override string Brand {get{return "UnitBatterySpy";}}
 public override int Priority {get{return 9000;}}
 public override bool IsExternalPackage {get{return true;}}
 public override bool MatchesVidPid(int v,int p){return v==Vid&&p==Pid;}
 public override bool CanHandle(MouseBatteryInfo dev){int v,p;return TryExtractDeviceVidPid(dev,out v,out p)&&v==Vid&&p==Pid;}
 public override List<MouseBatteryInfo> QueryDevices(){Calls++;return Results;}
}
'@
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw $compiled.Errors[0].ErrorText}
$aeSpy=$compiled.CompiledAssembly.CreateInstance('BatteryRefreshSpy');$aeSpy.Vid=0x260D;$aeSpy.Pid=0x1257
$rzSpy=$compiled.CompiledAssembly.CreateInstance('BatteryRefreshSpy');$rzSpy.Vid=0x1532;$rzSpy.Pid=0x00B0
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($aeSpy)
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($rzSpy)
$razer=New-Object RazerBatteryTray.MouseBatteryInfo;$razer.DeviceId='Razer:1532:00B0';$razer.BatteryPercent=41;$razer.IsSleeping=$true;$razer.IsConnected=$true;$razer.IsPrimary=$true;$razer.Transport='2.4G 无线';$razer.LastUpdated=[DateTime]::UtcNow.AddMinutes(-2)
$ae7=New-Object RazerBatteryTray.MouseBatteryInfo;$ae7.DeviceId='Dareu:260D:1257:receiver';$ae7.CustomName='AE7';$ae7.BatteryPercent=25
$fresh=New-Object RazerBatteryTray.MouseBatteryInfo;$fresh.DeviceId=$ae7.DeviceId;$fresh.BatteryPercent=87;$fresh.IsConnected=$true
$aeSpy.Results.Add($fresh);$rzSpy.Results.Add($razer)
$script:checks=0
function Check($ok,$name){if(-not $ok){throw $name};$script:checks++}
$result=[RazerBatteryTray.HardwareCommandBus]::QueryBatteryState($ae7)
Check ($result.BatteryPercent -eq 87 -and $aeSpy.Calls -eq 1 -and $rzSpy.Calls -eq 0) 'AE7 activity only queries its own driver, never Razer'
$list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]';$list.Add($razer);$list.Add($ae7)
$merged=[RazerBatteryTray.DeviceManager]::MergeDeviceUpdate($list,$result)
Check ([object]::ReferenceEquals($merged[0],$razer) -and $merged[0].BatteryPercent -eq 41 -and $merged[0].IsSleeping -and $merged[0].Transport -eq '2.4G 无线') 'Unrelated mouse value, sleep and transport are untouched'
Check ($merged[1].BatteryPercent -eq 87 -and $merged[1].CustomName -eq 'AE7' -and -not $merged[1].IsPrimary) 'Only the exact device updates and user naming is preserved'
Check ([object]::ReferenceEquals($list[1],$ae7) -and $ae7.BatteryPercent -eq 25) 'Merge does not mutate the old device list'
$aeSpy.Results.Clear();$aeSpy.Results.Add($razer)
Check ($null -eq [RazerBatteryTray.HardwareCommandBus]::QueryBatteryState($ae7)) 'A wrong identity returned by a driver is never accepted as the target'
Check ($rzSpy.Calls -eq 0) 'No full scan or unrelated fallback occurs'
Write-Host "PASS: $script:checks targeted battery refresh checks"
