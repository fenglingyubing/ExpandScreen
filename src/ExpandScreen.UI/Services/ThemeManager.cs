using System.Windows;
using ExpandScreen.Services.Configuration;

namespace ExpandScreen.UI.Services
{
    public static class ThemeManager
    {
        public static void ApplyTheme(ThemeMode theme)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var dictionaries = app.Resources.MergedDictionaries;
            Uri themeUri = theme == ThemeMode.Dark
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            int existingIndex = -1;
            for (int i = 0; i < dictionaries.Count; i++)
            {
                var source = dictionaries[i].Source?.ToString() ?? string.Empty;
                if (source.EndsWith("Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                    source.EndsWith("Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = i;
                    break;
                }
            }

            var themeDictionary = new ResourceDictionary { Source = themeUri };
            if (existingIndex >= 0)
            {
                dictionaries[existingIndex] = themeDictionary;
            }
            else
            {
                dictionaries.Add(themeDictionary);
            }
        }
    }
}

