using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using ExpandScreen.UI.ViewModels;

namespace ExpandScreen.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double click to maximize/restore
                MaximizeRestoreWindow();
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Drag window
                try
                {
                    DragMove();
                }
                catch
                {
                    // DragMove can throw exception if window state changes during drag
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            MaximizeRestoreWindow();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide to tray instead of closing
            Hide();
        }

        private void MaximizeRestoreWindow()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void DeviceCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DeviceViewModel device)
            {
                var mainViewModel = DataContext as MainViewModel;
                if (mainViewModel != null)
                {
                    mainViewModel.SelectedDevice = device;
                }
            }
        }
    }
}
