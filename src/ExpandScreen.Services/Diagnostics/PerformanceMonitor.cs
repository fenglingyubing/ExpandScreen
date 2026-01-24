using System.Diagnostics;
using ExpandScreen.Protocol.Network;

namespace ExpandScreen.Services.Diagnostics
{
    public sealed class PerformanceSnapshot
    {
        public DateTime TimestampUtc { get; set; }
        public double CpuUsagePercent { get; set; }
        public double WorkingSetMb { get; set; }
        public double ManagedHeapMb { get; set; }
        public double? CurrentFps { get; set; }
        public double? CurrentLatencyMs { get; set; }
        public double? LastHeartbeatRttMs { get; set; }
        public double? AverageHeartbeatRttMs { get; set; }
    }

    public sealed class PerformanceMonitor : IDisposable
    {
        private readonly Process _process;
        private readonly Stopwatch _watch;
        private TimeSpan _lastTotalProcessorTime;
        private long _lastCpuCheckTimeMs;
        private readonly object _lock = new();

        private double? _currentFps;
        private double? _currentLatencyMs;
        private NetworkSession? _session;

        public PerformanceMonitor(NetworkSession? session = null)
        {
            _process = Process.GetCurrentProcess();
            _watch = Stopwatch.StartNew();
            _lastTotalProcessorTime = _process.TotalProcessorTime;
            _lastCpuCheckTimeMs = 0;
            _session = session;
        }

        public void AttachSession(NetworkSession? session)
        {
            lock (_lock)
            {
                _session = session;
            }
        }

        public void RecordFps(double fps)
        {
            lock (_lock)
            {
                _currentFps = fps;
            }
        }

        public void RecordLatency(double latencyMs)
        {
            lock (_lock)
            {
                _currentLatencyMs = latencyMs;
            }
        }

        public PerformanceSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                _process.Refresh();
                var cpu = GetCpuUsageLocked();
                var workingSetMb = _process.WorkingSet64 / 1024.0 / 1024.0;
                var managedHeapMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0;

                double? lastRtt = null;
                double? avgRtt = null;
                var session = _session;
                if (session != null)
                {
                    var stats = session.GetStatistics();
                    lastRtt = stats.LastHeartbeatRttMs;
                    avgRtt = stats.AverageHeartbeatRttMs;
                }

                return new PerformanceSnapshot
                {
                    TimestampUtc = DateTime.UtcNow,
                    CpuUsagePercent = cpu,
                    WorkingSetMb = workingSetMb,
                    ManagedHeapMb = managedHeapMb,
                    CurrentFps = _currentFps,
                    CurrentLatencyMs = _currentLatencyMs,
                    LastHeartbeatRttMs = lastRtt,
                    AverageHeartbeatRttMs = avgRtt
                };
            }
        }

        public string BuildTextReport()
        {
            var snap = GetSnapshot();
            return
                "ExpandScreen Performance Snapshot\n" +
                "================================\n" +
                $"Time (UTC):           {snap.TimestampUtc:O}\n" +
                $"CPU usage:            {snap.CpuUsagePercent:F1}%\n" +
                $"Working set:          {snap.WorkingSetMb:F1} MB\n" +
                $"Managed heap:         {snap.ManagedHeapMb:F1} MB\n" +
                $"Current FPS:          {(snap.CurrentFps.HasValue ? snap.CurrentFps.Value.ToString("F1") : "N/A")}\n" +
                $"Current latency:      {(snap.CurrentLatencyMs.HasValue ? snap.CurrentLatencyMs.Value.ToString("F1") + " ms" : "N/A")}\n" +
                $"Heartbeat RTT (last): {(snap.LastHeartbeatRttMs.HasValue ? snap.LastHeartbeatRttMs.Value.ToString("F1") + " ms" : "N/A")}\n" +
                $"Heartbeat RTT (avg):  {(snap.AverageHeartbeatRttMs.HasValue ? snap.AverageHeartbeatRttMs.Value.ToString("F1") + " ms" : "N/A")}\n";
        }

        private double GetCpuUsageLocked()
        {
            try
            {
                var nowMs = _watch.ElapsedMilliseconds;
                var elapsedMs = nowMs - _lastCpuCheckTimeMs;
                if (elapsedMs <= 0)
                {
                    return 0;
                }

                var totalProcessorTime = _process.TotalProcessorTime;
                var cpuMs = (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

                _lastTotalProcessorTime = totalProcessorTime;
                _lastCpuCheckTimeMs = nowMs;

                var usage = cpuMs / elapsedMs * 100.0 / Environment.ProcessorCount;
                if (double.IsNaN(usage) || double.IsInfinity(usage))
                {
                    return 0;
                }

                return Math.Clamp(usage, 0, 100);
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
        }
    }
}

