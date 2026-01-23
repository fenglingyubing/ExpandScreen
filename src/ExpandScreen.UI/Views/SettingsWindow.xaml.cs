using System.Windows;
using System.Windows.Input;
using ExpandScreen.Services.Configuration;
using ExpandScreen.UI.Services;
using ExpandScreen.UI.ViewModels;

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
    }
}
