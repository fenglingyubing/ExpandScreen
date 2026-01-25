using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Services.Diagnostics;
using ExpandScreen.Services.Security;
using ExpandScreen.UI.Services;
using ExpandScreen.UI.ViewModels;
using ExpandScreen.Utils.Hotkeys;
using ExpandScreen.Utils;

namespace ExpandScreen.UI.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppConfig _originalConfig;
        private readonly ThemeMode _originalTheme;
        private readonly SettingsViewModel _viewModel;
        private bool _saved;

        public SettingsWindow()
        {
            InitializeComponent();

            if (Application.Current is not App app)
            {
                _originalConfig = AppConfig.CreateDefault();
                _originalTheme = ThemeMode.Dark;
                _viewModel = new SettingsViewModel(_originalConfig, configPath: "N/A");
                DataContext = _viewModel;
                return;
            }

            _originalConfig = app.ConfigService.GetSnapshot();
            _originalTheme = _originalConfig.General.Theme;

            _viewModel = new SettingsViewModel(_originalConfig, app.ConfigService.ConfigPath);
            _viewModel.ThemePreviewRequested += (_, theme) => ThemeManager.ApplyTheme(theme);
            DataContext = _viewModel;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndClose();
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要恢复所有设置为默认值吗？",
                "恢复默认设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.RestoreDefaults();
            }
        }

        private void RestoreHotkeys_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要恢复快捷键为默认值吗？",
                "恢复默认快捷键",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.RestoreDefaultHotkeys();
            }
        }

        private void RecordHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tag)
            {
                return;
            }

            var dialog = new HotkeyCaptureWindow
            {
                Owner = this
            };

            bool? ok = dialog.ShowDialog();
            if (ok != true)
            {
                return;
            }

            string capturedRaw = dialog.CapturedText?.Trim() ?? string.Empty;
            if (!HotkeyChord.TryParse(capturedRaw, out var chord))
            {
                System.Windows.MessageBox.Show("快捷键格式无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string captured = chord.ToString();
            if (chord.Modifiers == HotkeyModifiers.None && !chord.IsEmpty)
            {
                System.Windows.MessageBox.Show("建议至少包含一个修饰键（Ctrl/Alt/Shift）。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (TryGetConflict(tag, captured, out string conflictTag))
            {
                var result = System.Windows.MessageBox.Show(
                    $"该快捷键与“{GetActionDisplayName(conflictTag)}”冲突，是否覆盖？\n（覆盖后会清除冲突项）",
                    "快捷键冲突",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                SetHotkeyValue(conflictTag, string.Empty);
            }

            SetHotkeyValue(tag, captured);
        }

        private void ClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tag)
            {
                return;
            }

            SetHotkeyValue(tag, string.Empty);
        }

        private bool TryGetConflict(string tag, string captured, out string conflictTag)
        {
            conflictTag = string.Empty;
            if (string.IsNullOrWhiteSpace(captured))
            {
                return false;
            }

            foreach (var other in new[] { "ToggleMainWindow", "ConnectDisconnect", "NextDevice", "TogglePerformanceMode" })
            {
                if (string.Equals(other, tag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(GetHotkeyValue(other), captured, StringComparison.OrdinalIgnoreCase))
                {
                    conflictTag = other;
                    return true;
                }
            }

            return false;
        }

        private string GetHotkeyValue(string tag)
        {
            return tag switch
            {
                "ToggleMainWindow" => _viewModel.HotkeyToggleMainWindow,
                "ConnectDisconnect" => _viewModel.HotkeyConnectDisconnect,
                "NextDevice" => _viewModel.HotkeyNextDevice,
                "TogglePerformanceMode" => _viewModel.HotkeyTogglePerformanceMode,
                _ => string.Empty
            };
        }

        private void SetHotkeyValue(string tag, string value)
        {
            switch (tag)
            {
                case "ToggleMainWindow":
                    _viewModel.HotkeyToggleMainWindow = value;
                    break;
                case "ConnectDisconnect":
                    _viewModel.HotkeyConnectDisconnect = value;
                    break;
                case "NextDevice":
                    _viewModel.HotkeyNextDevice = value;
                    break;
                case "TogglePerformanceMode":
                    _viewModel.HotkeyTogglePerformanceMode = value;
                    break;
            }
        }

        private static string GetActionDisplayName(string tag)
        {
            return tag switch
            {
                "ToggleMainWindow" => "显示/隐藏主窗口",
                "ConnectDisconnect" => "连接/断开（当前设备）",
                "NextDevice" => "切换设备（下一个）",
                "TogglePerformanceMode" => "切换性能模式",
                _ => "未知操作"
            };
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app)
            {
                CancelAndClose();
                return;
            }

            var result = await app.ConfigService.SaveAsync(_viewModel.ToConfig());

            string message = result.Warnings.Count > 0
                ? $"设置已保存（已自动修正 {result.Warnings.Count} 项配置）。"
                : "设置已保存。";

            System.Windows.MessageBox.Show(message, "完成", MessageBoxButton.OK, MessageBoxImage.Information);

            _saved = true;
            Close();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var updateWindow = new UpdateWindow
            {
                Owner = this
            };
            updateWindow.ShowDialog();
        }

        private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            string logDir = AppPaths.GetLogDirectory();
            Directory.CreateDirectory(logDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }

        private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            try
            {
                var config = app.ConfigService.GetSnapshot();
                string zipPath = await DiagnosticsExportService.ExportAsync(config, app.ConfigService.ConfigPath);

                System.Windows.MessageBox.Show(
                    $"诊断包已导出：\n{zipPath}",
                    "导出完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Process.Start(new ProcessStartInfo
                {
                    FileName = zipPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"导出失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CopyCompatibilitySummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var compat = CompatibilitySnapshotCollector.Collect();
                var text = CompatibilitySnapshotCollector.BuildSummaryText(compat);
                Clipboard.SetText(text);
                System.Windows.MessageBox.Show("兼容性摘要已复制到剪贴板。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopySecuritySummary_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            try
            {
                var config = app.ConfigService.GetSnapshot();
                var snap = SecuritySnapshotCollector.Collect(config, app.ConfigService.ConfigPath);
                var text = SecuritySnapshotCollector.BuildSummaryText(snap);
                Clipboard.SetText(text);
                System.Windows.MessageBox.Show("安全摘要已复制到剪贴板。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndClose();
        }

        private void CancelAndClose()
        {
            if (!_saved)
            {
                ThemeManager.ApplyTheme(_originalTheme);
            }

            Close();
        }

        private void CopyPairingCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var code = _viewModel.WifiTlsPairingCode?.Trim();
                if (string.IsNullOrWhiteSpace(code) || code == "------")
                {
                    System.Windows.MessageBox.Show("当前无法获取配对码。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Clipboard.SetText(code);
                System.Windows.MessageBox.Show("配对码已复制到剪贴板。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RotateTlsCertificate_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要重置 WiFi TLS 配对吗？\n\n重置后：\n- 证书会重新生成，配对码将改变\n- 已配对的 Android 端需要删除旧信任并重新配对",
                "重置配对",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                bool ok = new TlsCertificateManager().RotateCertificate();
                _viewModel.RefreshTlsInfo();

                System.Windows.MessageBox.Show(
                    ok ? "已重置配对并生成新配对码。" : "未能删除旧证书文件，但已尝试刷新配对码。",
                    "完成",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"重置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
