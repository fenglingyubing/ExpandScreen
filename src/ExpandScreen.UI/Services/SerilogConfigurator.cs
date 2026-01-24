using System.Diagnostics;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Utils;
using Serilog;
using Serilog.Events;

namespace ExpandScreen.UI.Services
{
    public static class SerilogConfigurator
    {
        public static ILogger BuildLogger(LoggingConfig config)
        {
            var level = ParseLevel(config.MinimumLevel);
            string logDir = AppPaths.GetLogDirectory();
            Directory.CreateDirectory(logDir);

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .Enrich.WithProperty("App", "ExpandScreen");

            if (Debugger.IsAttached)
            {
#if DEBUG
                loggerConfig = loggerConfig.WriteTo.Console();
#endif
            }

            loggerConfig = loggerConfig.WriteTo.File(
                Path.Combine(logDir, "expandscreen-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetainedFileCountLimit,
                fileSizeLimitBytes: config.FileSizeLimitMb * 1024L * 1024L,
                rollOnFileSizeLimit: config.RollOnFileSizeLimit,
                shared: true);

            return loggerConfig.CreateLogger();
        }

        public static void Apply(LoggingConfig config)
        {
            var oldLogger = Log.Logger;
            Log.Logger = BuildLogger(config);
            if (oldLogger is IDisposable disposable)
            {
                disposable.Dispose();
            }

            LogMaintenance.CleanupOldLogs(AppPaths.GetLogDirectory(), retentionDays: config.RetentionDays);
        }

        private static LogEventLevel ParseLevel(string level)
        {
            if (Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return LogEventLevel.Information;
        }
    }
}
