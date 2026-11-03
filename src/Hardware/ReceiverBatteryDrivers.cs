// ============================================================================
// 模块名称: ReceiverBatteryDrivers.cs
// 核心职责: AE7 与 PD200W 精确端点、只读电量与缓存；AE7 独立活动状态区分待机和断开。
// 协议证据: docs/DEVICE_ADAPTATION_AE7_PD200W.md（NieRMHY 实机验证）。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0；UTF-8 BOM；禁止推测配置写入或仅凭共享 VID 匹配。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RazerBatteryTray
{
    internal static class ReceiverBatterySupport
    {
        internal static string ReceiverKey(HidDeviceDescriptor endpoint)
        {
            string identity = string.IsNullOrEmpty(endpoint.SerialNumber) ? endpoint.DevicePath : endpoint.SerialNumber;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes((identity ?? "").ToLowerInvariant()));
                return BitConverter.ToString(bytes, 0, 8).Replace("-", "");
            }
        }

        internal static MouseBatteryInfo Create(string id, string brand, string name, DeviceCategory category,
            HidDeviceDescriptor endpoint, bool connected)
        {
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = id;
            dev.Brand = brand;
            dev.DeviceName = name;
            dev.Category = category;
            dev.HardwareId = endpoint.DevicePath;
            dev.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", endpoint.VendorId, endpoint.ProductId);
            dev.Transport = "2.4G 无线";
            dev.IsDonglePresent = true;
            dev.IsConnected = connected;
            dev.BatteryPercent = -1;
            dev.ExactBatteryPercent = -1;
            int percent;
            bool charging;
            string cachedName;
            DateTime updated;
            if (DriverStateStore.TryLoadBatteryState(id, out percent, out charging, out cachedName, out updated))
            {
                dev.BatteryPercent = percent;
                dev.ExactBatteryPercent = percent;
                dev.LastUpdated = updated;
            }
            // 缓存只表示历史电量；离线时不能继承充电或在线状态。
            dev.IsChargingUnknown = true;
            return dev;
        }

        internal static void Apply(MouseBatteryInfo dev, int percent, bool charging, bool chargingKnown)
        {
            dev.BatteryPercent = percent;
            dev.ExactBatteryPercent = percent;
            dev.IsCharging = chargingKnown && charging;
            dev.IsChargingUnknown = !chargingKnown;
            dev.IsConnected = true;
            dev.IsSleeping = false;
            dev.LastUpdated = DateTime.Now;
            DriverStateStore.SaveBatteryState(dev.DeviceId, percent, dev.IsCharging, dev.DeviceName);
        }

        internal static bool Exchange(IntPtr handle, byte[] tx, int length, Predicate<byte[]> matches, out byte[] response)
        {
            response = null;
            if (!HidTransport.WriteReport(handle, tx, tx.Length, 200)) return false;
            Stopwatch deadline = Stopwatch.StartNew();
            // 不 DrainReports；用协议头和指令过滤异步通知，限制总时长与帧数。
            for (int i = 0; i < 24 && deadline.ElapsedMilliseconds < 350; i++)
            {
                byte[] rx;
                int remaining = Math.Max(1, 350 - (int)deadline.ElapsedMilliseconds);
                if (!HidTransport.ReadReportExact(handle, length, remaining, out rx)) return false;
                if (matches(rx)) { response = rx; return true; }
            }
            return false;
        }

        internal static void Populate(HardwareDriverBase driver, MouseBatteryInfo dev, string color)
        {
            if (dev == null) return;
            dev.DriverId = driver.DriverId;
            // 只开放电量；DPI、回报率、音频设置均未进行写入验证。
            dev.Capabilities = new DeviceCapabilities();
            dev.Capabilities.ThemeColorHex = color;
            dev.Capabilities.SensorDescription = "只读电量监测";
            dev.SupportedPollingRates = new int[0];
            dev.Capabilities.SupportedPollingRates = new int[0];
            dev.SupportsPollingRateWrite = false;
        }
    }

    public sealed class DareuAe7Driver : HardwareDriverBase
    {
        private static readonly object querySync = new object();
        public override string DriverId { get { return "BuiltIn:DareuAE7"; } }
        public override string Brand { get { return "Dareu"; } }
        public override bool MatchesVidPid(int vid, int pid) { return vid == 0x260D && pid == 0x1257; }
        public override bool CanHandle(MouseBatteryInfo dev)
        {
            int vid, pid;
            return dev != null && dev.Category == DeviceCategory.Mouse &&
                TryExtractDeviceVidPid(dev, out vid, out pid) && MatchesVidPid(vid, pid);
        }
        public static bool MatchesEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x260D && d.ProductId == 0x1257 &&
                d.UsagePage == 0xFF02 && d.Usage == 2 && d.InputReportLength == 17 &&
                d.OutputReportLength == 17 && d.FeatureReportLength == 0;
        }
        public static byte[] BuildBatteryQuery()
        {
            byte[] tx = new byte[17];
            tx[0] = 8; tx[1] = 4; tx[16] = 0x49;
            return tx;
        }
        public static byte[] BuildActivityQuery()
        {
            byte[] tx = new byte[17];
            tx[0] = 8; tx[1] = 3; tx[16] = 0x4A;
            return tx;
        }
        public static bool TryParseActivity(byte[] rx, out bool active)
        {
            active = false;
            if (rx == null || rx.Length != 17 || rx[0] != 8 || rx[1] != 3 ||
                rx[2] != 0 || rx[3] != 0 || rx[4] != 0 || rx[5] != 1 || rx[6] > 1) return false;
            int sum = 0;
            foreach (byte value in rx) sum += value;
            if ((sum & 255) != 0x55) return false;
            active = rx[6] == 1;
            return true;
        }
        public static bool TryParseBattery(byte[] rx, out int percent, out bool charging)
        {
            percent = -1; charging = false;
            if (rx == null || rx.Length != 17 || rx[0] != 8 || rx[1] != 4 ||
                rx[2] != 0 || rx[3] != 0 || rx[4] != 0 || rx[5] != 2 || rx[6] > 100 || rx[7] > 2) return false;
            int sum = 0;
            for (int i = 0; i < rx.Length; i++) sum += rx[i];
            if ((sum & 255) != 0x55) return false;
            percent = rx[6];
            charging = rx[7] == 1 || rx[7] == 2;
            return true;
        }
        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            ReceiverBatterySupport.Populate(this, dev, "#FF9800");
            if (dev != null) dev.Capabilities.SupportsBatteryWakeRefresh = true;
        }
        public override List<MouseBatteryInfo> QueryDevices()
        {
            lock (querySync) { return QueryDevicesCore(); }
        }
        private List<MouseBatteryInfo> QueryDevicesCore()
        {
            List<MouseBatteryInfo> devices = new List<MouseBatteryInfo>();
            foreach (HidDeviceDescriptor d in HidTransport.Enumerate(0x260D, 0x1257))
            {
                if (!MatchesEndpoint(d)) continue;
                MouseBatteryInfo dev = ReceiverBatterySupport.Create("Dareu:260D:1257:" +
                    ReceiverBatterySupport.ReceiverKey(d), Brand, "达尔优 AE7", DeviceCategory.Mouse, d, false);
                IntPtr handle = HidTransport.Open(d.DevicePath);
                try
                {
                    if (HidTransport.IsValidHandle(handle))
                    {
                        byte[] rx;
                        bool active = false;
                        bool activityKnown = ReceiverBatterySupport.Exchange(handle, BuildActivityQuery(), 17,
                            delegate(byte[] frame) { bool state; return TryParseActivity(frame, out state); }, out rx) &&
                            TryParseActivity(rx, out active);
                        if (activityKnown && !active)
                        {
                            dev.IsSleeping = true;
                        }
                        else
                        {
                            // 一次短暂丢包不能决定状态；只对同一只读电量命令有限重试。
                            for (int attempt = 0; attempt < 2; attempt++)
                            {
                                if (attempt != 0) System.Threading.Thread.Sleep(80);
                                if (!ReceiverBatterySupport.Exchange(handle, BuildBatteryQuery(), 17,
                                    delegate(byte[] frame) { int p; bool c; return TryParseBattery(frame, out p, out c); }, out rx)) continue;
                                int percent; bool charging;
                                if (TryParseBattery(rx, out percent, out charging))
                                    ReceiverBatterySupport.Apply(dev, percent, charging, true);
                                break;
                            }
                        }
                    }
                }
                finally { HidTransport.Close(handle); }
                PopulateCapabilities(dev);
                devices.Add(dev);
            }
            return devices;
        }
    }

    public sealed class MaonoPd200wDriver : HardwareDriverBase
    {
        public override string DriverId { get { return "BuiltIn:MaonoPD200W"; } }
        public override string Brand { get { return "Maono"; } }
        public override bool MatchesVidPid(int vid, int pid) { return vid == 0x352F && pid == 0x040E; }
        public override bool CanHandle(MouseBatteryInfo dev)
        {
            int vid, pid;
            return dev != null && dev.Category == DeviceCategory.Microphone &&
                TryExtractDeviceVidPid(dev, out vid, out pid) && MatchesVidPid(vid, pid);
        }
        public static bool MatchesEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x352F && d.ProductId == 0x040E &&
                d.UsagePage == 0x000C && d.Usage == 1 && d.InputReportLength == 64 &&
                d.OutputReportLength == 64 && d.FeatureReportLength == 0;
        }
        public static byte[] BuildReadQuery(int address)
        {
            // 仅允许已核实的连接状态、电量、充电状态寄存器；0x03 配置写入从不发送。
            if (address != 0x41 && address != 0x42 && address != 0x49)
                throw new ArgumentOutOfRangeException("address");
            byte[] tx = new byte[64];
            tx[0] = 0xC4; tx[1] = 9; tx[4] = 4; tx[5] = (byte)address;
            int checksum = (-(0xC4 + 9 + 4 + address)) & 65535;
            tx[7] = (byte)checksum; tx[8] = (byte)(checksum >> 8);
            return tx;
        }
        public static bool TryParseRegister(byte[] rx, int address, out int value)
        {
            value = -1;
            if (address != 0x41 && address != 0x42 && address != 0x49) return false;
            if (rx == null || rx.Length != 64 || rx[0] != 0xC4 || rx[3] != 0 || rx[4] != 4) return false;
            int length = rx[1] | (rx[2] << 8);
            if (length < 11 || length > rx.Length || (length - 7) % 4 != 0) return false;
            int sum = 0;
            for (int i = 0; i < length - 2; i++) sum += rx[i];
            sum += rx[length - 2] | (rx[length - 1] << 8);
            if ((sum & 65535) != 0) return false;
            for (int i = 5; i < length - 2; i += 4)
            {
                if ((rx[i] | (rx[i + 1] << 8)) != address) continue;
                int packed = rx[i + 2] | (rx[i + 3] << 8);
                if (address == 0x42 && (rx[i + 2] > 100 || rx[i + 3] > 100)) return false;
                value = packed;
                return true;
            }
            return false;
        }
        private static bool ReadRegister(IntPtr handle, int address, out int value)
        {
            value = -1;
            byte[] rx;
            if (!ReceiverBatterySupport.Exchange(handle, BuildReadQuery(address), 64,
                delegate(byte[] frame) { int v; return TryParseRegister(frame, address, out v); }, out rx)) return false;
            return TryParseRegister(rx, address, out value);
        }
        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            ReceiverBatterySupport.Populate(this, dev, "#E85D5D");
        }
        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> devices = new List<MouseBatteryInfo>();
            foreach (HidDeviceDescriptor d in HidTransport.Enumerate(0x352F, 0x040E))
            {
                if (!MatchesEndpoint(d)) continue;
                int links = -1, battery = -1, charge = -1;
                IntPtr handle = HidTransport.Open(d.DevicePath);
                try
                {
                    if (HidTransport.IsValidHandle(handle))
                    {
                        ReadRegister(handle, 0x41, out links);
                        if (links > 0)
                        {
                            ReadRegister(handle, 0x42, out battery);
                            ReadRegister(handle, 0x49, out charge);
                        }
                    }
                }
                finally { HidTransport.Close(handle); }
                for (int slot = 0; slot < 2; slot++)
                {
                    string id = "Maono:352F:040E:" + ReceiverBatterySupport.ReceiverKey(d) + ":TX" + (slot + 1);
                    bool connected = links >= 0 && ((links >> (slot * 8)) & 255) == 2;
                    MouseBatteryInfo dev = ReceiverBatterySupport.Create(id, Brand,
                        "闪克无线麦克风 TX" + (slot + 1) + " (PD200W 接收器)", DeviceCategory.Microphone, d, connected);
                    if (connected && battery >= 0)
                    {
                        int percent = (battery >> (slot * 8)) & 255;
                        int state = charge < 0 ? -1 : ((charge >> (slot * 8)) & 255);
                        // 当前仅实测到非充电 0；其余状态保持未知，不虚报充电。
                        ReceiverBatterySupport.Apply(dev, percent, false, state == 0);
                    }
                    PopulateCapabilities(dev);
                    // 空闲通道不生成假 0% 设备；曾在线的通道保留离线历史卡片。
                    if (connected || dev.BatteryPercent >= 0) devices.Add(dev);
                }
            }
            return devices;
        }
    }
}
