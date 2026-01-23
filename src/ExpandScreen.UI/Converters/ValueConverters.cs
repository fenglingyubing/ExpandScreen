using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ExpandScreen.UI.ViewModels;

namespace ExpandScreen.UI.Converters
{
    /// <summary>
    /// Converts DeviceStatus to a color brush
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DeviceStatus status)
            {
                return status switch
                {
                    DeviceStatus.Connected => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118)), // Green
                    DeviceStatus.Connecting => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 179, 0)), // Orange
                    DeviceStatus.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82)), // Red
                    _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)) // Gray
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean to visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = parameter?.ToString()?.ToLower() == "invert";
                bool result = invert ? !boolValue : boolValue;
                return result ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts DeviceStatus to animation state (for pulsing effect)
    /// </summary>
    public class StatusToAnimationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DeviceStatus status)
            {
                return status == DeviceStatus.Connecting || status == DeviceStatus.Connected;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
            {
                return 0d;
            }

            if (values[0] is not double actualWidth || actualWidth <= 0)
            {
                return 0d;
            }

            double value = values[1] is double v ? v : 0d;
            double maximum = values[2] is double m ? m : 100d;
            if (maximum <= 0)
            {
                return 0d;
            }

            double ratio = Math.Clamp(value / maximum, 0d, 1d);
            return actualWidth * ratio;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
