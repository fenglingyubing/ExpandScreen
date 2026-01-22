using System.Diagnostics;
using ExpandScreen.Utils;

namespace ExpandScreen.IntegrationTests
{
    /// <summary>
    /// 性能监控和分析工具
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly Process _currentProcess;
        private readonly PerformanceCounter? _cpuCounter;
        private readonly Stopwatch _totalTimeWatch;
        private long _frameCount;
        private long _totalEncodeTime;
        private long _totalSendTime;
        private readonly List<long> _frameTimes;

        public PerformanceMonitor()
        {
            _currentProcess = Process.GetCurrentProcess();
            _totalTimeWatch = Stopwatch.StartNew();
            _frameTimes = new List<long>();

            try
            {
                // 尝试创建CPU计数器（可能在某些环境下失败）
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }
            catch
            {
                LogHelper.Warning("无法创建CPU性能计数器");
            }
        }

        /// <summary>
        /// 记录一帧的处理
        /// </summary>
        public void RecordFrame(long encodeTimeMs, long sendTimeMs)
        {
            _frameCount++;
            _totalEncodeTime += encodeTimeMs;
            _totalSendTime += sendTimeMs;
            _frameTimes.Add(encodeTimeMs + sendTimeMs);
        }

        /// <summary>
        /// 获取当前性能统计
        /// </summary>
        public PerformanceStats GetStats()
        {
            var stats = new PerformanceStats
            {
                TotalFrames = _frameCount,
                ElapsedTimeMs = _totalTimeWatch.ElapsedMilliseconds,
                AverageEncodeTimeMs = _frameCount > 0 ? _totalEncodeTime / (double)_frameCount : 0,
                AverageSendTimeMs = _frameCount > 0 ? _totalSendTime / (double)_frameCount : 0,
                AverageFps = _totalTimeWatch.ElapsedMilliseconds > 0 ?
                    _frameCount * 1000.0 / _totalTimeWatch.ElapsedMilliseconds : 0,
                MemoryUsageMB = _currentProcess.WorkingSet64 / 1024.0 / 1024.0,
                CpuUsagePercent = GetCpuUsage()
            };

            if (_frameTimes.Count > 0)
            {
                stats.MinFrameTimeMs = _frameTimes.Min();
                stats.MaxFrameTimeMs = _frameTimes.Max();
                stats.MedianFrameTimeMs = GetMedian(_frameTimes);
            }

            return stats;
        }

        /// <summary>
        /// 打印性能报告
        /// </summary>
        public void PrintReport()
        {
            var stats = GetStats();

            LogHelper.Info("==========================================");
            LogHelper.Info("         性能分析报告");
            LogHelper.Info("==========================================");
            LogHelper.Info($"总帧数:           {stats.TotalFrames}");
            LogHelper.Info($"总耗时:           {stats.ElapsedTimeMs}ms ({stats.ElapsedTimeMs / 1000.0:F1}s)");
            LogHelper.Info($"平均FPS:          {stats.AverageFps:F1}");
            LogHelper.Info("------------------------------------------");
            LogHelper.Info($"平均编码时间:     {stats.AverageEncodeTimeMs:F2}ms");
            LogHelper.Info($"平均发送时间:     {stats.AverageSendTimeMs:F2}ms");
            LogHelper.Info($"最小帧时间:       {stats.MinFrameTimeMs}ms");
            LogHelper.Info($"最大帧时间:       {stats.MaxFrameTimeMs}ms");
            LogHelper.Info($"中位帧时间:       {stats.MedianFrameTimeMs}ms");
            LogHelper.Info("------------------------------------------");
            LogHelper.Info($"内存使用:         {stats.MemoryUsageMB:F1}MB");
            LogHelper.Info($"CPU使用率:        {stats.CpuUsagePercent:F1}%");
            LogHelper.Info("==========================================");

            // 性能评估
            LogHelper.Info("");
            LogHelper.Info("性能评估:");

            if (stats.AverageFps >= 60)
                LogHelper.Info("✅ FPS优秀 (>= 60fps)");
            else if (stats.AverageFps >= 30)
                LogHelper.Info("⚠️  FPS良好 (>= 30fps)");
            else
                LogHelper.Error("❌ FPS不足 (< 30fps)");

            if (stats.AverageEncodeTimeMs < 16)
                LogHelper.Info("✅ 编码延迟优秀 (< 16ms)");
            else if (stats.AverageEncodeTimeMs < 33)
                LogHelper.Info("⚠️  编码延迟良好 (< 33ms)");
            else
                LogHelper.Error("❌ 编码延迟过高 (>= 33ms)");

            if (stats.CpuUsagePercent < 30)
                LogHelper.Info("✅ CPU占用优秀 (< 30%)");
            else if (stats.CpuUsagePercent < 50)
                LogHelper.Info("⚠️  CPU占用良好 (< 50%)");
            else
                LogHelper.Warning("⚠️  CPU占用较高 (>= 50%)");

            if (stats.MemoryUsageMB < 500)
                LogHelper.Info("✅ 内存占用优秀 (< 500MB)");
            else if (stats.MemoryUsageMB < 1000)
                LogHelper.Info("⚠️  内存占用良好 (< 1GB)");
            else
                LogHelper.Warning("⚠️  内存占用较高 (>= 1GB)");
        }

        private double GetCpuUsage()
        {
            try
            {
                return _cpuCounter?.NextValue() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private long GetMedian(List<long> values)
        {
            var sorted = values.OrderBy(x => x).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
        }
    }

    /// <summary>
    /// 性能统计数据
    /// </summary>
    public class PerformanceStats
    {
        public long TotalFrames { get; set; }
        public long ElapsedTimeMs { get; set; }
        public double AverageEncodeTimeMs { get; set; }
        public double AverageSendTimeMs { get; set; }
        public double AverageFps { get; set; }
        public double MemoryUsageMB { get; set; }
        public double CpuUsagePercent { get; set; }
        public long MinFrameTimeMs { get; set; }
        public long MaxFrameTimeMs { get; set; }
        public long MedianFrameTimeMs { get; set; }
    }

    /// <summary>
    /// 性能测试用例
    /// </summary>
    public class PerformanceTests
    {
        [Fact]
        public void Test_PerformanceMonitor_BasicUsage()
        {
            using var monitor = new PerformanceMonitor();

            // 模拟一些帧处理
            for (int i = 0; i < 100; i++)
            {
                monitor.RecordFrame(10, 5);
                Thread.Sleep(10);
            }

            var stats = monitor.GetStats();

            Assert.True(stats.TotalFrames == 100);
            Assert.True(stats.AverageEncodeTimeMs == 10);
            Assert.True(stats.AverageSendTimeMs == 5);

            monitor.PrintReport();
        }
    }
}
