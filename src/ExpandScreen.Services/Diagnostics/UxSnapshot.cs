using ExpandScreen.Services.Configuration;

namespace ExpandScreen.Services.Diagnostics
{
    public sealed class UxSnapshot
    {
        public DateTime TimestampUtc { get; set; }

        public string? AppVersion { get; set; }
        public string? AppInformationalVersion { get; set; }

        public ThemeMode ConfigTheme { get; set; }

        public bool IsWindows { get; set; }
        public int? SystemDpi { get; set; }
        public double? SystemScale { get; set; }

        public bool? HighContrastEnabled { get; set; }
        public string? HighContrastScheme { get; set; }

        public bool? ScreenReaderPresent { get; set; }
        public bool? KeyboardCuesEnabled { get; set; }

        public bool? ClientAreaAnimationEnabled { get; set; }
        public bool? UiEffectsEnabled { get; set; }
    }
}

