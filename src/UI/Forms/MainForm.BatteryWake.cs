// ============================================================================
// 模块名称: MainForm.BatteryWake.cs
// 核心职责: 按真实输入只刷新对应待机设备；合并事件、保留其他设备状态并发布局部快照。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0；仅按设备能力匹配，不在 UI 硬编码品牌或主动唤醒硬件。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private readonly ReceiverBatteryWakePolicy receiverBatteryWakePolicy = new ReceiverBatteryWakePolicy();
        private readonly Dictionary<IntPtr, string> receiverWakeDevicePaths = new Dictionary<IntPtr, string>();

        private void HandleReceiverBatteryWakeInput(IntPtr rawInput)
        {
            if (!ReceiverBatteryWakePolicy.HasPending(activeDevices) || PauseBackgroundRefresh) return;
            int size = Marshal.SizeOf(typeof(RAWINPUTHEADER));
            RAWINPUTHEADER header;
            if (GetRawInputData(rawInput, RID_HEADER, out header, ref size, size) < 0 ||
                header.dwType != 0 || header.hDevice == IntPtr.Zero) return;
            string targetId;
            if (!receiverWakeDevicePaths.TryGetValue(header.hDevice, out targetId))
            {
                uint length = 0;
                GetRawInputDeviceInfoW(header.hDevice, RIDI_DEVICENAME, null, ref length);
                if (length == 0 || length > 32768) return;
                StringBuilder buffer = new StringBuilder((int)length);
                if (GetRawInputDeviceInfoW(header.hDevice, RIDI_DEVICENAME, buffer, ref length) <= 0) return;
                MouseBatteryInfo match = ReceiverBatteryWakePolicy.FindTarget(activeDevices, buffer.ToString());
                targetId = match == null ? "" : match.DeviceId;
                // 无关鼠标也缓存为拒绝，避免每个高频包重复解析硬件路径。
                receiverWakeDevicePaths[header.hDevice] = targetId;
            }
            if (string.IsNullOrEmpty(targetId)) return;
            MouseBatteryInfo target = activeDevices.Find(delegate(MouseBatteryInfo dev) { return dev.DeviceId == targetId; });
            if (ReceiverBatteryWakePolicy.IsPending(target)) ScheduleTargetedBatteryRefresh(target);
        }

        private void ScheduleTargetedBatteryRefresh(MouseBatteryInfo target)
        {
            if (target == null || PauseBackgroundRefresh || !receiverBatteryWakePolicy.TrySchedule(DateTime.UtcNow)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Thread.Sleep(200);
                    if (PauseBackgroundRefresh || IsDisposed || Disposing) return;
                    MouseBatteryInfo update = HardwareCommandBus.QueryBatteryState(target);
                    if (update == null) return;
                    BeginInvoke(new Action(delegate
                    {
                        if (IsDisposed || Disposing || PauseBackgroundRefresh) return;
                        activeDevices = DeviceManager.MergeDeviceUpdate(activeDevices, update);
                        receiverWakeDevicePaths.Clear();
                        if (selectedDevice != null && selectedDevice.DeviceId == update.DeviceId)
                        {
                            selectedDevice = update;
                            UpdateDeviceTabsUI();
                            UpdateUI(update, false);
                            UpdatePrimaryPinButton(update);
                        }
                        if (update.IsPrimary)
                        {
                            lastInfo = update;
                            UpdateTrayIcon(update.BatteryPercent, update.IsCharging, update.IsConnected, update.IsSleeping);
                            UpdateStatusDisplay();
                        }
                        EnableMouseWakeDetector(isMouseSleeping);
                        UpdateTrayDeviceMenuItems();
                        BatterySnapshotExporter.PublishPartial(activeDevices);
                    }));
                }
                catch { }
                finally { receiverBatteryWakePolicy.Complete(); }
            });
        }
    }
}
