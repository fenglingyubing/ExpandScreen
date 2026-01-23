using System.Windows;
using ExpandScreen.Services.Configuration;
using ExpandScreen.UI.Services;
using Serilog;

namespace ExpandScreen.UI
{
    public partial class App : System.Windows.Application
    {
        private TrayIconService? _trayIconService;
        public bool IsShuttingDown { get; private set; }
        public ConfigService ConfigService { get; } = new();

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

            ShutdownStarted += (_, _) => IsShuttingDown = true;

            // Load config + apply (theme, autostart, minimize-to-tray behavior via MainWindow)
            var config = ConfigService.LoadAsync().GetAwaiter().GetResult();
            ThemeManager.ApplyTheme(config.General.Theme);
            AutoStartService.Apply(config.General.AutoStart);

            ConfigService.ConfigChanged += (_, args) =>
            {
                ThemeManager.ApplyTheme(args.Config.General.Theme);
                AutoStartService.Apply(args.Config.General.AutoStart);
            };
            ConfigService.StartWatching();

            // Initialize tray icon
            _trayIconService = new TrayIconService();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            ConfigService.Dispose();
            Log.Information("ExpandScreen 退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
