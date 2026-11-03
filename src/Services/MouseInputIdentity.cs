// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0。真实鼠标输入只按目标设备的精确 VID/PID 匹配。
using System;
namespace RazerBatteryTray
{
    public static class MouseInputIdentity
    {
        public static bool Matches(string path, MouseBatteryInfo target)
        {
            if (target == null || target.Category != DeviceCategory.Mouse || string.IsNullOrEmpty(path) ||
                path.IndexOf("VHF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ROOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("HIDI2C", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            int sourceVid, sourcePid, targetVid, targetPid;
            // DeviceId 可能统一有线/无线型号；HardwareId 才是当前实际接口的 PID。
            bool hasTarget = HardwareDriverBase.TryExtractVidPidFromString(target.HardwareId, out targetVid, out targetPid) ||
                HardwareDriverBase.TryExtractDeviceVidPid(target, out targetVid, out targetPid);
            return hasTarget && HardwareDriverBase.TryExtractVidPidFromString(path, out sourceVid, out sourcePid) &&
                sourceVid == targetVid && sourcePid == targetPid;
        }
    }
}
