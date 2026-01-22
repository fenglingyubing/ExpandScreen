using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace ExpandScreen.UI.Services
{
    /// <summary>
    /// Manages system tray icon and context menu
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        public TrayIconService()
        {
            _notifyIcon = new NotifyIcon
            {
                // Create a simple icon (in production, use a proper .ico file)
                Icon = CreateIcon(),
                Text = "ExpandScreen",
                Visible = true
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();

            contextMenu.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("退出", null, (s, e) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private Icon CreateIcon()
        {
            // Create a simple colored icon (16x16)
            // In production, load from Assets/app.ico
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(Color.FromArgb(0, 217, 255)), 2, 2, 12, 12);
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }

        private void ShowMainWindow()
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }

        private void ExitApplication()
        {
            Application.Current.Shutdown();
        }

        public void ShowBalloonTip(string title, string text, int timeout = 3000)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _notifyIcon?.Dispose();
            _disposed = true;
        }
    }
}
