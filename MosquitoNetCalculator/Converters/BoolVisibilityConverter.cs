using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>Converts bool to Visibility: true→Visible, false→Collapsed.</summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>Converts bool to inverse Visibility: true→Collapsed, false→Visible.</summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v != Visibility.Visible;
    }

    /// <summary>Converts an empty string to Visible and a non-empty string to Collapsed.</summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible ? string.Empty : null!;
    }

    /// <summary>Converts an empty string to Collapsed and a non-empty string to Visible.</summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class NonEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible ? "value" : string.Empty;
    }
}
