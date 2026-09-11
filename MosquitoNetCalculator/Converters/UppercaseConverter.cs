using System;
using System.Globalization;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// v3.50.1 (prototype parity): renders DataGrid column captions in upper case
    /// WITHOUT mutating the Header string itself. MainWindow's BeginningEdit
    /// handler compares e.Column.Header with exact column names to lock cells,
    /// so the source strings must remain untouched (business logic preserved).
    /// </summary>
    public sealed class UppercaseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString()?.ToUpper(culture ?? CultureInfo.CurrentUICulture) ?? string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}