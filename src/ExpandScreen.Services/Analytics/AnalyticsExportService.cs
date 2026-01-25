using System.Text.Json;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Analytics
{
    public static class AnalyticsExportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static async Task<string> ExportAsync(
            AnalyticsSnapshot snapshot,
            string? outputDirectory = null,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string outDir = string.IsNullOrWhiteSpace(outputDirectory)
                ? AppPaths.GetDiagnosticsDirectory()
                : outputDirectory!;

            Directory.CreateDirectory(outDir);

            string path = Path.Combine(outDir, $"expandscreen-analytics-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            return path;
        }
    }
}

