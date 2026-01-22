using System.Windows;
using ExpandScreen.UI.Services;
using Serilog;

namespace ExpandScreen.UI
{
    public partial class App : Application
    {
        private TrayIconService? _trayIconService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 配置Serilog日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/expandscreen-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("ExpandScreen 启动");

            // Initialize tray icon
            _trayIconService = new TrayIconService();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            Log.Information("ExpandScreen 退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
