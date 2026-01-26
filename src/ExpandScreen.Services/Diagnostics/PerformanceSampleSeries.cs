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

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
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

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await writer.WriteLineAsync(string.Join(",",
                "timestampUtc",
                "cpuUsagePercent",
                "workingSetMb",
                "managedHeapMb",
                "currentFps",
                "currentLatencyMs",
                "lastHeartbeatRttMs",
                "averageHeartbeatRttMs")).ConfigureAwait(false);

            var row = new StringBuilder(capacity: 128);
            foreach (var s in Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                row.Clear();
                row.Append(s.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',');
                row.Append(s.CpuUsagePercent.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                row.Append(s.WorkingSetMb.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                row.Append(s.ManagedHeapMb.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                row.Append(NullableDouble(s.CurrentFps)).Append(',');
                row.Append(NullableDouble(s.CurrentLatencyMs)).Append(',');
                row.Append(NullableDouble(s.LastHeartbeatRttMs)).Append(',');
                row.Append(NullableDouble(s.AverageHeartbeatRttMs));

                await writer.WriteLineAsync(row.ToString()).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }

        public string BuildQuickSummary()
        {
            int count = Samples.Count;
            if (count == 0)
            {
                return "No samples.";
            }

            double cpuSum = 0;
            double cpuMax = double.MinValue;
            double memSum = 0;
            double memMax = double.MinValue;

            foreach (var sample in Samples)
            {
                cpuSum += sample.CpuUsagePercent;
                cpuMax = Math.Max(cpuMax, sample.CpuUsagePercent);

                memSum += sample.WorkingSetMb;
                memMax = Math.Max(memMax, sample.WorkingSetMb);
            }

            double cpuAvg = cpuSum / count;
            double memAvg = memSum / count;

            return $"Samples: {count}, Duration: {Duration.TotalSeconds:F1}s, " +
                   $"CPU avg/max: {cpuAvg:F1}%/{cpuMax:F1}%, " +
                   $"WorkingSet avg/max: {memAvg:F0}/{memMax:F0} MB";
        }

        private static string NullableDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("F2", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
