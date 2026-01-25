using System.Windows;
using ExpandScreen.Services.Analytics;
using ExpandScreen.Services.Configuration;
using ExpandScreen.UI.Services;
using ExpandScreen.UI.ViewModels;
using Serilog;

namespace ExpandScreen.UI
{
    public partial class App : System.Windows.Application
    {
        private TrayIconService? _trayIconService;
        private GlobalHotkeyService? _hotkeyService;
        public bool IsShuttingDown { get; private set; }
        public ConfigService ConfigService { get; } = new();
        public AnalyticsService AnalyticsService { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Bootstrap Serilog (reconfigured after config load)
            SerilogConfigurator.Apply(new LoggingConfig());
            Log.Information("ExpandScreen 启动");

            ShutdownStarted += (_, _) => IsShuttingDown = true;

            // Load config + apply (theme, autostart, minimize-to-tray behavior via MainWindow)
            var config = ConfigService.LoadAsync().GetAwaiter().GetResult();
            SerilogConfigurator.Apply(config.Logging);
            ThemeManager.ApplyTheme(config.General.Theme);
            AutoStartService.Apply(config.General.AutoStart);

            AnalyticsService.InitializeAsync().GetAwaiter().GetResult();
            AnalyticsService.ApplyOptions(ToAnalyticsOptions(config));
            AnalyticsService.TrackAppStarted();

            ConfigService.ConfigChanged += (_, args) =>
            {
                SerilogConfigurator.Apply(args.Config.Logging);
                ThemeManager.ApplyTheme(args.Config.General.Theme);
                AutoStartService.Apply(args.Config.General.AutoStart);
                AnalyticsService.ApplyOptions(ToAnalyticsOptions(args.Config));

                if (_hotkeyService != null)
                {
                    var warnings = _hotkeyService.ApplyConfig(args.Config);
                    if (warnings.Count > 0)
                    {
                        Log.Warning("Hotkeys applied with {WarningCount} warning(s): {Warnings}", warnings.Count, string.Join("; ", warnings));
                    }
                }
            };
            ConfigService.StartWatching();

            // Initialize tray icon
            _trayIconService = new TrayIconService();
        }

        private static AnalyticsOptions ToAnalyticsOptions(AppConfig config)
        {
            return new AnalyticsOptions
            {
                Enabled = config.Analytics.Enabled,
                MaxHistoryEntries = config.Analytics.MaxHistoryEntries,
                MaxPerformanceSamples = config.Analytics.MaxPerformanceSamples,
                PerformanceSampleIntervalSeconds = config.Analytics.PerformanceSampleIntervalSeconds
            };
        }

        internal void InitializeHotkeys(Window mainWindow)
        {
            if (_hotkeyService != null)
            {
                return;
            }

            _hotkeyService = new GlobalHotkeyService(mainWindow);
            var warnings = _hotkeyService.ApplyConfig(ConfigService.GetSnapshot());
            if (warnings.Count > 0)
            {
                Log.Warning("Hotkeys applied with {WarningCount} warning(s): {Warnings}", warnings.Count, string.Join("; ", warnings));
            }
            _hotkeyService.HotkeyPressed += (_, action) => Dispatcher.InvokeAsync(() => DispatchHotkeyAction(action));
        }

        private async Task DispatchHotkeyAction(HotkeyAction action)
        {
            var window = MainWindow;
            var viewModel = window?.DataContext as MainViewModel;

            switch (action)
            {
                case HotkeyAction.ToggleMainWindow:
                    ToggleMainWindowVisibility();
                    break;

                case HotkeyAction.ConnectDisconnect:
                    viewModel?.ToggleConnectDisconnectSelected();
                    break;

                case HotkeyAction.NextDevice:
                    viewModel?.SelectNextDevice();
                    break;

                case HotkeyAction.TogglePerformanceMode:
                    if (viewModel != null)
                    {
                        await viewModel.CyclePerformanceModeAsync();
                    }
                    break;
            }
        }

        private void ToggleMainWindowVisibility()
        {
            var window = MainWindow;
            if (window == null)
            {
                return;
            }

            if (window.IsVisible && window.WindowState != WindowState.Minimized && window.IsActive)
            {
                window.Hide();
                return;
            }

            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Dispose();
            _trayIconService?.Dispose();
            ConfigService.Dispose();
            try
            {
                AnalyticsService.FlushAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
            AnalyticsService.Dispose();
            Log.Information("ExpandScreen 退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
