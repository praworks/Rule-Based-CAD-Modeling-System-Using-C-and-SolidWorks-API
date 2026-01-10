using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AICAD.UI
{
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;
            try
            {
                var name = parameter.ToString();
                var enumName = value.ToString();
                return string.Equals(enumName, name, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { return Visibility.Collapsed; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ApiTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = (value as string ?? string.Empty).Trim().ToLowerInvariant();
            string key;
            if (type == "modify")
            {
                key = "ApiModifyBrush";
            }
            else if (type == "selection")
            {
                key = "ApiSelectionBrush";
            }
            else if (type == "file_load")
            {
                key = "ApiFileLoadBrush";
            }
            else if (type == "view_change")
            {
                key = "ApiViewChangeBrush";
            }
            else
            {
                key = "ApiDefaultBrush";
            }

            var brush = Application.Current.TryFindResource(key) as Brush;
            return brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
