// ============================================================================
// 模块名称: BatterySnapshotExporter.cs
// 核心职责: 将已完成轮询的设备状态原子导出为本机只读 JSON，供 TrafficMonitor 复用。
// 不发起硬件查询或网络监听；默认关闭，HKCU ExportBatteryStatus=1 时启用。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0，UTF-8 BOM；设备标识脱敏，错误不能影响硬件轮询。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class BatterySnapshotExporter
    {
        private static readonly object sync = new object();
        private static readonly string session = Guid.NewGuid().ToString("N");
        private static bool cleanupRegistered;

        public static string SnapshotPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrisPulse", "battery-status.json"); }
        }

        public static void Publish(List<MouseBatteryInfo> devices)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\RazerBatteryTray", false))
                {
                    if (key == null || Convert.ToInt32(key.GetValue("ExportBatteryStatus", 0)) != 1) return;
                }
                lock (sync)
                {
                    if (!cleanupRegistered)
                    {
                        AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(); };
                        cleanupRegistered = true;
                    }
                    WriteSnapshot(SnapshotPath, BuildJson(devices, DateTime.UtcNow, Process.GetCurrentProcess().Id));
                }
            }
            catch (Exception ex) { Debug.WriteLine("Battery snapshot export: " + ex.Message); }
        }

        // 同目录替换保证读取方只见完整旧帧或完整新帧，不见半写入 JSON。
        public static void WriteSnapshot(string path, string json)
        {
            string temporary = path + "." + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void Cleanup()
        {
            try
            {
                lock (sync)
                {
                    if (File.Exists(SnapshotPath) && File.ReadAllText(SnapshotPath).IndexOf(session, StringComparison.Ordinal) >= 0)
                        File.Delete(SnapshotPath);
                }
            }
            catch { }
        }

        public static string BuildJson(List<MouseBatteryInfo> devices, DateTime generatedUtc, int producerPid)
        {
            StringBuilder output = new StringBuilder();
            output.Append("{\"code\":200,\"message\":\"ok\",\"producerPid\":").Append(producerPid);
            output.Append(",\"producerSession\":").Append(Json(session));
            output.Append(",\"data\":{\"schemaVersion\":1,\"generatedAtUtc\":").Append(Json(generatedUtc.ToUniversalTime().ToString("o")));
            output.Append(",\"devices\":[");
            bool first = true;
            if (devices != null) foreach (MouseBatteryInfo device in devices)
            {
                if (device == null) continue;
                if (!first) output.Append(',');
                first = false;
                output.Append("{\"id\":").Append(Json(HashId(device.DeviceId)));
                output.Append(",\"name\":").Append(Json(device.DeviceName ?? device.DisplayName));
                output.Append(",\"renamedName\":").Append(Json(string.IsNullOrEmpty(device.CustomName) ? null : device.CustomName));
                output.Append(",\"source\":").Append(Json(device.Brand ?? "unknown"));
                output.Append(",\"status\":").Append(Json(device.IsConnected ? "online" : "offline"));
                output.Append(",\"battery\":");
                if (device.BatteryPercent >= 0 && device.BatteryPercent <= 100) output.Append(device.BatteryPercent);
                else output.Append("null");
                output.Append(",\"isCharging\":").Append(device.IsConnected && !device.IsSleeping && !device.IsChargingUnknown && device.IsCharging ? "true" : "false");
                output.Append(",\"isSleeping\":").Append(device.IsSleeping ? "true" : "false");
                output.Append(",\"isBatteryUnsupported\":").Append(device.Capabilities.SupportsBattery ? "false" : "true");
                output.Append(",\"isShownInTray\":").Append(device.IsPrimary ? "true" : "false");
                output.Append(",\"batteryLastUpdatedUtc\":").Append(Json(device.LastUpdated == DateTime.MinValue ? null : device.LastUpdated.ToUniversalTime().ToString("o")));
                output.Append('}');
            }
            output.Append("]}}");
            return output.ToString();
        }

        private static string HashId(string id)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id ?? "")), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        private static string Json(string value)
        {
            if (value == null) return "null";
            StringBuilder text = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
    }
}
