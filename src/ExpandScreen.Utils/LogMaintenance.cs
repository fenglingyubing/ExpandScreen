using System;
using System.IO;

namespace ExpandScreen.Utils
{
    public static class LogMaintenance
    {
        public static void CleanupOldLogs(string logDirectory, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(logDirectory) || retentionDays <= 0)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(logDirectory))
                {
                    return;
                }

                var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
                foreach (var file in Directory.EnumerateFiles(logDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var lastWriteUtc = info.LastWriteTimeUtc;
                        if (lastWriteUtc < cutoffUtc)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}

