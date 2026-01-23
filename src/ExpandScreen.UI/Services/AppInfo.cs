using System.Reflection;

namespace ExpandScreen.UI.Services
{
    public static class AppInfo
    {
        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

        public static string DisplayVersion
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly is null)
                {
                    return CurrentVersion.ToString();
                }

                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    return informational!;
                }

                return CurrentVersion.ToString();
            }
        }
    }
}

