using System.Windows;
using Serilog;

namespace ExpandScreen.UI
{
    public partial class App : Application
    {
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
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("ExpandScreen 退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
