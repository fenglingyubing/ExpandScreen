using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Diagnostics
{
    public static class DiagnosticsExportService
    {
        public static async Task<string> ExportAsync(
            AppConfig configSnapshot,
            string configPath,
            string? performanceReport = null,
            string? outputDirectory = null,
            CancellationToken cancellationToken = default)
        {
            string outDir = string.IsNullOrWhiteSpace(outputDirectory)
                ? AppPaths.GetDiagnosticsDirectory()
                : outputDirectory!;

            Directory.CreateDirectory(outDir);

            string zipPath = Path.Combine(outDir, $"expandscreen-diagnostics-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            // System info
            var systemInfo = new
            {
                TimestampUtc = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                OSDescription = RuntimeInformation.OSDescription,
                OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                Is64BitProcess = Environment.Is64BitProcess,
                ProcessorCount = Environment.ProcessorCount
            };
            await AddJsonAsync(zip, "system-info.json", systemInfo, cancellationToken).ConfigureAwait(false);

            // Compatibility info (best-effort)
            try
            {
                var compat = CompatibilitySnapshotCollector.Collect();
                await AddJsonAsync(zip, "compatibility-info.json", compat, cancellationToken).ConfigureAwait(false);
                await AddTextAsync(zip, "compatibility-summary.txt", CompatibilitySnapshotCollector.BuildSummaryText(compat), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AddTextAsync(zip, "compatibility-info.error.txt", ex.ToString(), cancellationToken).ConfigureAwait(false);
            }

            // Security info (best-effort)
            try
            {
                var security = SecuritySnapshotCollector.Collect(configSnapshot, configPath);
                await AddJsonAsync(zip, "security-info.json", security, cancellationToken).ConfigureAwait(false);
                await AddTextAsync(zip, "security-summary.txt", SecuritySnapshotCollector.BuildSummaryText(security), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AddTextAsync(zip, "security-info.error.txt", ex.ToString(), cancellationToken).ConfigureAwait(false);
            }

            // Config snapshot
            await AddJsonAsync(zip, "config.json", configSnapshot, cancellationToken).ConfigureAwait(false);
            await AddTextAsync(zip, "config-path.txt", configPath, cancellationToken).ConfigureAwait(false);

            // Try to include raw config file (best-effort)
            try
            {
                if (File.Exists(configPath))
                {
                    zip.CreateEntryFromFile(configPath, "config.raw.json");
                }
            }
            catch
            {
                // best-effort
            }

            // Performance report
            if (string.IsNullOrWhiteSpace(performanceReport))
            {
                performanceReport = new PerformanceMonitor().BuildTextReport();
            }
            await AddTextAsync(zip, "performance-report.txt", performanceReport, cancellationToken).ConfigureAwait(false);

            // Logs
            string logDir = AppPaths.GetLogDirectory();
            try
            {
                if (Directory.Exists(logDir))
                {
                    foreach (var file in Directory.EnumerateFiles(logDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            zip.CreateEntryFromFile(file, $"logs/{fileName}");
                        }
                        catch
                        {
                            // best-effort
                        }
                    }
                }
            }
            catch
            {
                // best-effort
            }

            return zipPath;
        }

        private static async Task AddTextAsync(ZipArchive zip, string entryName, string content, CancellationToken cancellationToken)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        private static async Task AddJsonAsync<T>(ZipArchive zip, string entryName, T value, CancellationToken cancellationToken)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
        }
    }
}
