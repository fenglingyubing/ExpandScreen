using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using ExpandScreen.UI.ViewModels;
using System;

namespace ExpandScreen.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (Application.Current is App app)
            {
                app.InitializeHotkeys(this);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            var app = Application.Current as App;
            bool shuttingDown = app?.IsShuttingDown == true || Application.Current?.Dispatcher?.HasShutdownStarted == true;

            if (!shuttingDown && ShouldMinimizeToTray())
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                Hide();
            }
            else
            {
                e.Cancel = false;
            }

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
            if (ShouldMinimizeToTray())
            {
                Hide();
                return;
            }

            Close();
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

        private static bool ShouldMinimizeToTray()
        {
            if (Application.Current is not App app)
            {
                return true;
            }

            try
            {
                return app.ConfigService.GetSnapshot().General.MinimizeToTray;
            }
            catch
            {
                return true;
            }
        }
    }
}
