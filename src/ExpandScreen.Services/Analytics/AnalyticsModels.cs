using System.Security.Cryptography;
using System.Text;

namespace ExpandScreen.Services.Analytics
{
    public sealed class AnalyticsOptions
    {
        public bool Enabled { get; set; }
        public int MaxHistoryEntries { get; set; } = 500;
        public int MaxPerformanceSamples { get; set; } = 720; // ~2h at 10s
        public int PerformanceSampleIntervalSeconds { get; set; } = 10;
    }

    public sealed class AnalyticsStore
    {
        public int SchemaVersion { get; set; } = 1;
        public string InstallId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public int LaunchCount { get; set; }
        public double TotalAppSeconds { get; set; }
        public int TotalConnections { get; set; }
        public double TotalConnectedSeconds { get; set; }

        public Dictionary<string, int> FeatureUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AnalyticsEvent> History { get; set; } = new();
        public List<AnalyticsPerformanceSample> PerformanceSamples { get; set; } = new();
    }

    public sealed class AnalyticsEvent
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string>? Data { get; set; }
    }

    public sealed class AnalyticsPerformanceSample
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public double CpuUsagePercent { get; set; }
        public double WorkingSetMb { get; set; }
        public double ManagedHeapMb { get; set; }
    }

    public sealed class AnalyticsSnapshot
    {
        public bool Enabled { get; set; }
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        public int LaunchCount { get; set; }
        public TimeSpan TotalAppTime { get; set; }
        public int TotalConnections { get; set; }
        public TimeSpan TotalConnectedTime { get; set; }

        public IReadOnlyDictionary<string, int> FeatureUsage { get; set; } = new Dictionary<string, int>();
        public IReadOnlyList<AnalyticsEvent> History { get; set; } = Array.Empty<AnalyticsEvent>();
        public IReadOnlyList<AnalyticsPerformanceSample> PerformanceSamples { get; set; } = Array.Empty<AnalyticsPerformanceSample>();
    }

    internal static class AnalyticsAnonymizer
    {
        public static string HashDeviceId(string installId, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return "unknown";
            }

            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes($"{installId}:{deviceId}");
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant()[..12];
        }
    }
}

