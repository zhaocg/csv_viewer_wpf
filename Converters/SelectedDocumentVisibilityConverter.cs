using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CsvViewer.Converters;

public sealed class SelectedDocumentVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Length >= 2 && ReferenceEquals(values[0], values[1]) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
