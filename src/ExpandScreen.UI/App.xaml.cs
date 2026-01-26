using System.Windows;
using System.Windows.Threading;
using ExpandScreen.Services.Analytics;
using ExpandScreen.Services.Configuration;
using ExpandScreen.UI.Services;
using ExpandScreen.UI.ViewModels;
using ExpandScreen.UI.Views;
using ExpandScreen.Utils;
using Serilog;

namespace ExpandScreen.UI
{
    public partial class App : System.Windows.Application
    {
        private TrayIconService? _trayIconService;
        private GlobalHotkeyService? _hotkeyService;
        private int _isHandlingUnhandledException;
        public bool IsShuttingDown { get; private set; }
        public ConfigService ConfigService { get; } = new();
        public AnalyticsService AnalyticsService { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterGlobalExceptionHandlers();

            // Bootstrap Serilog (reconfigured after config load)
            SerilogConfigurator.Apply(new LoggingConfig());
            Log.Information("ExpandScreen 启动");

            Dispatcher.ShutdownStarted += (_, _) => IsShuttingDown = true;

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

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (IsShuttingDown)
            {
                return;
            }

            if (Interlocked.Exchange(ref _isHandlingUnhandledException, 1) == 1)
            {
                return;
            }

            try
            {
                LogHelper.Error("[UI] DispatcherUnhandledException", e.Exception);

                bool shouldContinue = false;
                try
                {
                    var dialog = new UnhandledExceptionDialog(e.Exception);
                    if (MainWindow != null && MainWindow.IsLoaded)
                    {
                        dialog.Owner = MainWindow;
                    }

                    var result = dialog.ShowDialog();
                    shouldContinue = result == true;
                }
                catch (Exception ex)
                {
                    LogHelper.Error("[UI] Failed to show exception dialog.", ex);
                    try
                    {
                        MessageBox.Show(
                            $"应用遇到错误并需要关闭。\n\n{e.Exception.GetBaseException().Message}",
                            "ExpandScreen",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch
                    {
                    }
                }

                e.Handled = true;

                if (!shouldContinue)
                {
                    IsShuttingDown = true;
                    Shutdown(-1);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isHandlingUnhandledException, 0);
            }
        }

        private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogHelper.Error("[Fatal] AppDomain UnhandledException", ex);
                }
                else
                {
                    LogHelper.Error($"[Fatal] AppDomain UnhandledException (non-Exception): {e.ExceptionObject}");
                }
            }
            catch
            {
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                LogHelper.Error("[BG] UnobservedTaskException", e.Exception);
                e.SetObserved();
            }
            catch
            {
            }
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
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
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
