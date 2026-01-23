using System.Globalization;
using System.Windows.Data;
using ExpandScreen.Services.Configuration;

namespace ExpandScreen.UI.Converters
{
    public sealed class ConfigEnumDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                ThemeMode.Dark => "深色",
                ThemeMode.Light => "浅色",
                VideoEncoderPreference.Auto => "自动（推荐）",
                VideoEncoderPreference.Nvenc => "NVENC（NVIDIA）",
                VideoEncoderPreference.QuickSync => "QuickSync（Intel）",
                VideoEncoderPreference.FFmpeg => "FFmpeg（软件）",
                PerformanceMode.Balanced => "均衡",
                PerformanceMode.LowLatency => "低延迟",
                PerformanceMode.Quality => "高质量",
                _ => value?.ToString() ?? string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

