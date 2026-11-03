// ============================================================================
// 模块名称: BatterySnapshotPublisher.cs
// 核心职责: 独立心跳发布不可变设备观察快照；启停、失败重试和退出清理不依赖硬件查询。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4.0；心跳不得更新 observedAtUtc 或触发任何 HID 访问。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RazerBatteryTray
{
    public sealed class BatterySnapshotPublisher : IDisposable
    {
        private readonly object sync = new object();
        private readonly string path;
        private readonly Func<bool> isEnabled;
        private readonly int intervalMs;
        private readonly string session = Guid.NewGuid().ToString("N");
        private string devicesJson = "[]";
        private DateTime? observedUtc;
        private Timer timer;
        private bool disposed;

        public BatterySnapshotPublisher(string path, Func<bool> isEnabled, int intervalMs)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Snapshot path is required", "path");
            if (isEnabled == null) throw new ArgumentNullException("isEnabled");
            if (intervalMs < 100) throw new ArgumentOutOfRangeException("intervalMs");
            this.path = path;
            this.isEnabled = isEnabled;
            this.intervalMs = intervalMs;
        }

        public void Start()
        {
            lock (sync)
            {
                if (disposed || timer != null) return;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                timer = new Timer(Heartbeat, null, 0, intervalMs);
            }
        }

        public void Publish(List<MouseBatteryInfo> devices)
        {
            // 在硬件查询线程立即序列化；定时线程不持有可被 UI 修改的设备对象。
            string serialized = BatterySnapshotExporter.SerializeDevices(devices);
            lock (sync)
            {
                if (disposed) return;
                devicesJson = serialized;
                observedUtc = DateTime.UtcNow;
                WriteCurrent(observedUtc.Value);
            }
        }

        private void Heartbeat(object state)
        {
            lock (sync)
            {
                if (!disposed) WriteCurrent(DateTime.UtcNow);
            }
        }

        private void WriteCurrent(DateTime generatedUtc)
        {
            try
            {
                if (!isEnabled()) { CleanupOwnedSnapshot(); return; }
                BatterySnapshotExporter.WriteSnapshot(path, BatterySnapshotExporter.BuildEnvelope(
                    devicesJson, generatedUtc, observedUtc, Process.GetCurrentProcess().Id, session, intervalMs));
            }
            catch (Exception ex)
            {
                // 文件暂时被占用或目录被清理时，下一次心跳继续重试。
                Debug.WriteLine("Battery snapshot heartbeat: " + ex.Message);
            }
        }

        private void CleanupOwnedSnapshot()
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).IndexOf(session, StringComparison.Ordinal) >= 0)
                    File.Delete(path);
            }
            catch { }
        }

        private void OnProcessExit(object sender, EventArgs args) { Dispose(); }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (timer != null) { timer.Dispose(); timer = null; }
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                CleanupOwnedSnapshot();
            }
        }
    }
}
