// ============================================================================
// 模块名称: MainForm.BatteryWake.cs
// 核心职责: 监听非主鼠标的真实 RawInput，待机接收器活动后合并刷新电量与共享快照。
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
            string path;
            if (!receiverWakeDevicePaths.TryGetValue(header.hDevice, out path))
            {
                uint length = 0;
                GetRawInputDeviceInfoW(header.hDevice, RIDI_DEVICENAME, null, ref length);
                if (length == 0 || length > 32768) return;
                StringBuilder buffer = new StringBuilder((int)length);
                if (GetRawInputDeviceInfoW(header.hDevice, RIDI_DEVICENAME, buffer, ref length) <= 0) return;
                path = buffer.ToString();
                receiverWakeDevicePaths[header.hDevice] = path;
            }
            if (ReceiverBatteryWakePolicy.FindTarget(activeDevices, path) == null ||
                !receiverBatteryWakePolicy.TrySchedule(DateTime.UtcNow)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(200);
                QueryAndApplyDeviceStatus(false, receiverBatteryWakePolicy.Complete);
            });
        }
    }
}
