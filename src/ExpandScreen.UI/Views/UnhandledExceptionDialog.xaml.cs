using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ExpandScreen.Utils;

namespace ExpandScreen.UI.Views
{
    public partial class UnhandledExceptionDialog : Window
    {
        private readonly string _details;

        public UnhandledExceptionDialog(Exception exception)
        {
            InitializeComponent();

            _details = BuildDetails(exception);
            DataContext = new UnhandledExceptionDialogModel(exception, _details);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_details);
            }
            catch
            {
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string BuildDetails(Exception exception)
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "unknown";
            var now = DateTimeOffset.Now;
            var logDir = AppPaths.GetLogDirectory();

            var sb = new StringBuilder(capacity: 4096);
            sb.AppendLine("ExpandScreen Unhandled Exception Report");
            sb.AppendLine("=====================================");
            sb.AppendLine($"Time:        {now:O}");
            sb.AppendLine($"Version:     {version}");
            sb.AppendLine($"OS:          {Environment.OSVersion}");
            sb.AppendLine($"Framework:   {Environment.Version}");
            sb.AppendLine($"Process:     {Environment.ProcessId}");
            sb.AppendLine($"LogDir:      {logDir}");
            sb.AppendLine();
            sb.AppendLine(exception.ToString());
            return sb.ToString();
        }
    }

    internal sealed class UnhandledExceptionDialogModel
    {
        public UnhandledExceptionDialogModel(Exception exception, string details)
        {
            Details = details;

            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "unknown";

            Summary = exception.GetBaseException().Message;
            TimestampText = DateTimeOffset.Now.ToString("O");
            VersionText = $"v{version}";
            LogDirText = AppPaths.GetLogDirectory();
        }

        public string Summary { get; }
        public string TimestampText { get; }
        public string VersionText { get; }
        public string LogDirText { get; }
        public string Details { get; }
    }
}
