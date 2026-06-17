using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EspionSpotify.Wpf
{
    /// <summary>true -> Collapsed, false -> Visible. Used to show the masked field when not revealed.</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v != Visibility.Visible;
    }
}
