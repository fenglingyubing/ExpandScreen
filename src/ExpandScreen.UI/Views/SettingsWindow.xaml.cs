using System.Windows;
using System.Windows.Input;

namespace ExpandScreen.UI.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要恢复所有设置为默认值吗？",
                "恢复默认设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // TODO: Implement restore defaults logic
                MessageBox.Show("设置已恢复为默认值", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement save settings logic
            MessageBox.Show("设置已保存", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
