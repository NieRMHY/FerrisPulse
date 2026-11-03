// ============================================================================
// 模块名称: ReceiverBatteryWakePolicy.cs
// 核心职责: 按能力和精确 VID/PID 匹配待机接收器，合并鼠标活动触发的电量刷新。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0；不修改设备在线状态或电量，不触发配置写入。
// ============================================================================
using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public sealed class ReceiverBatteryWakePolicy
    {
        private readonly object sync = new object();
        private bool queued;
        private DateTime lastScheduled = DateTime.MinValue;

        public static bool IsPending(MouseBatteryInfo dev)
        {
            return dev != null && dev.Category == DeviceCategory.Mouse && dev.IsDonglePresent &&
                dev.Capabilities.SupportsBatteryWakeRefresh && (!dev.IsConnected || dev.IsSleeping);
        }

        public static bool HasPending(List<MouseBatteryInfo> devices)
        {
            if (devices != null) foreach (MouseBatteryInfo dev in devices) if (IsPending(dev)) return true;
            return false;
        }

        public static MouseBatteryInfo FindTarget(List<MouseBatteryInfo> devices, string path)
        {
            if (devices == null || string.IsNullOrEmpty(path) ||
                path.IndexOf("VHF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ROOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("HIDI2C", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            int inputVid, inputPid;
            if (!HardwareDriverBase.TryExtractVidPidFromString(path, out inputVid, out inputPid)) return null;
            foreach (MouseBatteryInfo dev in devices)
            {
                int vid, pid;
                if (IsPending(dev) && HardwareDriverBase.TryExtractDeviceVidPid(dev, out vid, out pid) &&
                    vid == inputVid && pid == inputPid) return dev;
            }
            return null;
        }

        public static string GetStatusText(MouseBatteryInfo dev)
        {
            if (IsPending(dev)) return dev.IsSleeping ? "接收器在线 · 待机" : "接收器在线 · 等待电量";
            return dev != null && dev.IsConnected ? "已连接" : "设备已离线";
        }

        public bool TrySchedule(DateTime now)
        {
            lock (sync)
            {
                if (queued || (now - lastScheduled).TotalSeconds < 2) return false;
                queued = true;
                lastScheduled = now;
                return true;
            }
        }

        public void Complete() { lock (sync) { queued = false; } }
    }
}
