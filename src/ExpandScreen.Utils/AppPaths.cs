using System;
using System.IO;

namespace ExpandScreen.Utils
{
    public static class AppPaths
    {
        public static string GetRoamingAppDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExpandScreen");
        }

        public static string GetLocalAppDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpandScreen");
        }

        public static string GetLogDirectory()
        {
            return Path.Combine(GetLocalAppDataDirectory(), "logs");
        }

        public static string GetDiagnosticsDirectory()
        {
            return Path.Combine(GetLocalAppDataDirectory(), "diagnostics");
        }
    }
}

