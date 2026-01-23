using System.Diagnostics;
using Microsoft.Win32;

namespace ExpandScreen.UI.Services
{
    public static class AutoStartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ExpandScreen";

        public static bool IsSupported => OperatingSystem.IsWindows();

        public static void Apply(bool enable)
        {
            if (!IsSupported)
            {
                return;
            }

            try
            {
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

                if (runKey == null)
                {
                    return;
                }

                if (!enable)
                {
                    runKey.DeleteValue(AppName, throwOnMissingValue: false);
                    return;
                }

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return;
                }

                runKey.SetValue(AppName, $"\"{exePath}\"");
            }
            catch
            {
                // best-effort
            }
        }
    }
}

