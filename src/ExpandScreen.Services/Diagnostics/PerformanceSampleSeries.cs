using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExpandScreen.Services.Diagnostics
{
    public sealed class PerformanceSampleSeries
    {
        public DateTime StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public List<PerformanceSnapshot> Samples { get; set; } = new();

        public TimeSpan Duration => EndedUtc > StartedUtc ? EndedUtc - StartedUtc : TimeSpan.Zero;

        public async Task ExportJsonAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                    stream,
                    this,
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",",
                "timestampUtc",
                "cpuUsagePercent",
                "workingSetMb",
                "managedHeapMb",
                "currentFps",
                "currentLatencyMs",
                "lastHeartbeatRttMs",
                "averageHeartbeatRttMs"));

            foreach (var s in Samples)
            {
                sb.AppendLine(string.Join(",",
                    Csv(s.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(s.CpuUsagePercent.ToString("F2", CultureInfo.InvariantCulture)),
                    Csv(s.WorkingSetMb.ToString("F2", CultureInfo.InvariantCulture)),
                    Csv(s.ManagedHeapMb.ToString("F2", CultureInfo.InvariantCulture)),
                    Csv(NullableDouble(s.CurrentFps)),
                    Csv(NullableDouble(s.CurrentLatencyMs)),
                    Csv(NullableDouble(s.LastHeartbeatRttMs)),
                    Csv(NullableDouble(s.AverageHeartbeatRttMs))));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }

        public string BuildQuickSummary()
        {
            if (Samples.Count == 0)
            {
                return "No samples.";
            }

            double cpuAvg = Samples.Average(s => s.CpuUsagePercent);
            double cpuMax = Samples.Max(s => s.CpuUsagePercent);
            double memAvg = Samples.Average(s => s.WorkingSetMb);
            double memMax = Samples.Max(s => s.WorkingSetMb);

            return $"Samples: {Samples.Count}, Duration: {Duration.TotalSeconds:F1}s, " +
                   $"CPU avg/max: {cpuAvg:F1}%/{cpuMax:F1}%, " +
                   $"WorkingSet avg/max: {memAvg:F0}/{memMax:F0} MB";
        }

        private static string NullableDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("F2", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!needsQuoting)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}

