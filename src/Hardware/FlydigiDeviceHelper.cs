// ============================================================================
// 模块名称: FlydigiDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   飞智 (Flydigi) APEX 6 / APEX 6 Pro / Vader 5 Pro 手柄私有 GPA6 协议适配：
//   精确 0xFFA0/Usage 1/33B Vendor HID 接口、5A A5 帧、命令 0x01 电量解析；
//   型号优先使用 USB 产品名；设备关闭后保留有原始采样时间的离线历史卡片。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改帧格式、电量刻度或设备识别时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class FlydigiDeviceHelper
    {
        private static readonly object hidLock = new object();

        public const int VendorIdFlydigi = 0x37D7;
        public const int PidApex6 = 0x2502;
        public const int PidVader5Pro = 0x2401;

        private const int VendorUsagePage = 0xFFA0;
        private const int InfoCommand = 0x01;
        private const int FrameLength = 32;
        private const int ReadTimeoutMs = 450;
        private const int QueryThrottleMs = 900;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid != VendorIdFlydigi) return false;
            return pid == PidApex6 || pid == PidVader5Pro;
        }

        public static bool IsFlydigiDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_37d7") || lower.Contains("37d7:") || lower.Contains("37d7&");
            if (!isVid) return false;
            return lower.Contains("pid_2502") || lower.Contains("pid_2401") ||
                   lower.Contains(":2502") || lower.Contains(":2401");
        }

        public static string GetModelName(int productId, int deviceType, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string product = productString.Replace("Controller (", "").Replace(")", "").Trim();
                if (product.IndexOf("APEX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    product.IndexOf("Vader", StringComparison.OrdinalIgnoreCase) >= 0) return product;
            }
            if (deviceType == 150) return "Flydigi APEX 6 Pro";
            if (deviceType == 149) return "Flydigi APEX 6";
            if (deviceType == 152) return "Flydigi APEX 6 Pro 幻刃";
            if (deviceType == 151) return "Flydigi APEX 6 Pro";

            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Replace("Controller (", "").Replace(")", "").Trim();
                if (cleaned.Length > 0) return cleaned;
            }
            return productId == PidVader5Pro ? "Flydigi Vader 5 Pro" : "Flydigi APEX 6";
        }

        public static string GetTransportName(int connectionMode)
        {
            // APEX 6 实机 2.4G 接收器同样返回模式 1，尚不能以此字段判断物理链路。
            return "USB / 2.4G";
        }

        public static bool IsBatteryEndpoint(HidDeviceDescriptor descriptor)
        {
            return descriptor != null && IsSupportedVidPid(descriptor.VendorId, descriptor.ProductId) &&
                descriptor.UsagePage == VendorUsagePage && descriptor.Usage == 1 &&
                descriptor.InputReportLength == FrameLength + 1 && descriptor.OutputReportLength == FrameLength + 1 &&
                descriptor.FeatureReportLength == 0;
        }

        public static MouseBatteryInfo CreateOfflineDevice(int productId, HidDeviceDescriptor endpoint)
        {
            if (!IsSupportedVidPid(VendorIdFlydigi, productId) || (endpoint != null && !IsBatteryEndpoint(endpoint))) return null;
            string id = string.Format("Flydigi:{0:X4}:{1:X4}", VendorIdFlydigi, productId);
            int battery; bool charging; string name; DateTime updated;
            bool saved = DriverStateStore.TryLoadBatteryState(id, out battery, out charging, out name, out updated);
            if (!saved && endpoint == null) return null;
            return new MouseBatteryInfo
            {
                DeviceId = id, HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdFlydigi, productId),
                HardwareId = endpoint == null ? id : endpoint.DevicePath,
                DeviceName = saved && !string.IsNullOrEmpty(name) ? name : GetModelName(productId, 0, endpoint == null ? null : endpoint.ProductString),
                Brand = "Flydigi", Category = DeviceCategory.Gamepad, Transport = "USB / 2.4G",
                IsConnected = false, IsSleeping = false, IsDonglePresent = endpoint != null,
                IsCharging = false, IsChargingUnknown = true, BatteryPercent = saved ? battery : -1,
                ExactBatteryPercent = saved ? battery : -1, LastUpdated = saved ? updated : DateTime.MinValue
            };
        }

        /// <summary>
        /// 解析命令 0x01 设备信息回复。raw 数据可带或不带前导 Report ID 0。
        /// GPA6 私有帧: 5A A5 01 01 00 [payload 25B] ... checksum
        /// payload[6] 低 4 位为电量档位，高 4 位为充电标志。
        /// </summary>
        public static bool ParseInfo(byte[] report, int length, out int batteryPercent, out bool isCharging, out int deviceType, out int connectionMode)
        {
            batteryPercent = -1;
            isCharging = false;
            deviceType = 0;
            connectionMode = 0;
            if (report == null || length < 12) return false;

            int magic = -1;
            if (report[0] == 0x5A && report[1] == 0xA5) magic = 0;
            else if (report[0] == 0x00 && report[1] == 0x5A && report[2] == 0xA5) magic = 1;
            if (magic < 0) return false;
            if (report[magic + 2] != InfoCommand) return false;
            if (length < magic + 12) return false;

            int payload = magic + 5;
            deviceType = report[payload];
            connectionMode = report[payload + 1];
            int raw = report[payload + 6] & 0x0F;
            isCharging = ((report[payload + 6] >> 4) & 0x0F) != 0;

            if (raw <= 5) batteryPercent = raw * 20;
            else if (raw <= 10) batteryPercent = raw * 10;
            else if (raw <= 100) batteryPercent = raw;
            else batteryPercent = -1;

            if (batteryPercent < 0) return false;
            if (batteryPercent > 100) batteryPercent = 100;
            return true;
        }

        private static byte[] BuildInfoFrame()
        {
            byte[] frame = new byte[FrameLength];
            frame[0] = 0x5A;
            frame[1] = 0xA5;
            frame[2] = InfoCommand;
            frame[3] = 0x02;
            int sum = 0;
            for (int i = 2; i <= 30; i++) sum += frame[i];
            frame[31] = (byte)(sum & 0xFF);
            return frame;
        }

        private static bool QueryDevice(HidDeviceDescriptor descriptor, out MouseBatteryInfo info)
        {
            info = null;
            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.HIDP_CAPS caps = HidTransport.GetCaps(handle);
                int outputLength = caps.OutputReportByteLength;
                int inputLength = caps.InputReportByteLength;
                if (outputLength < FrameLength + 1 || inputLength < FrameLength + 1)
                {
                    outputLength = descriptor.OutputReportLength;
                    inputLength = descriptor.InputReportLength;
                }
                if (outputLength < FrameLength + 1 || inputLength < FrameLength + 1) return false;

                HidTransport.DrainReports(handle, inputLength, 6);

                byte[] frame = BuildInfoFrame();
                byte[] tx = new byte[outputLength];
                // Windows HID 要求 unnumbered 报告包含前导 0 Report ID。
                for (int i = 0; i < frame.Length; i++) tx[i + 1] = frame[i];
                if (!HidTransport.WriteReport(handle, tx, outputLength, 600)) return false;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    byte[] rx;
                    if (!HidTransport.ReadReport(handle, inputLength, ReadTimeoutMs, out rx)) continue;
                    int battery;
                    bool charging;
                    int deviceType;
                    int connectionMode;
                    if (!ParseInfo(rx, inputLength, out battery, out charging, out deviceType, out connectionMode)) continue;

                    info = new MouseBatteryInfo();
                    info.DeviceId = string.Format("Flydigi:{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
                    info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", descriptor.VendorId, descriptor.ProductId);
                    info.HardwareId = descriptor.InstanceId;
                    info.DeviceName = GetModelName(descriptor.ProductId, deviceType, descriptor.ProductString);
                    info.Brand = "Flydigi";
                    info.Category = DeviceCategory.Gamepad;
                    info.Transport = GetTransportName(connectionMode);
                    info.IsCustom = false;
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsDonglePresent = true;
                    info.IsCharging = charging;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.LastUpdated = DateTime.Now;
                    DriverStateStore.SaveBatteryObservation(info.DeviceId, battery, charging, info.DeviceName, info.LastUpdated);
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        public static List<MouseBatteryInfo> QueryAllFlydigiDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdFlydigi, 0);
                int seenApex6 = 0;
                int seenVader5 = 0;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor descriptor = descriptors[i];
                    if (!IsBatteryEndpoint(descriptor)) continue;
                    if (descriptor.ProductId == PidApex6 && seenApex6 > 0) continue;
                    if (descriptor.ProductId == PidVader5Pro && seenVader5 > 0) continue;

                    MouseBatteryInfo info;
                    if (!QueryDevice(descriptor, out info) || info == null) info = CreateOfflineDevice(descriptor.ProductId, descriptor);
                    if (info != null)
                    {
                        result.Add(info);
                        if (descriptor.ProductId == PidApex6) seenApex6++;
                        else seenVader5++;
                    }
                }
                // 接收器可能在手柄关机时隐藏控制接口；仅恢复真实保存过的历史设备。
                foreach (int productId in new int[] { PidApex6, PidVader5Pro })
                {
                    bool seen = productId == PidApex6 ? seenApex6 > 0 : seenVader5 > 0;
                    if (seen) continue;
                    MouseBatteryInfo offline = CreateOfflineDevice(productId, null);
                    if (offline != null) result.Add(offline);
                }
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
