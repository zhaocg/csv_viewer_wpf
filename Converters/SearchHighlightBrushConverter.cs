using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CsvViewer.Converters;

public sealed class SearchHighlightBrushConverter : IMultiValueConverter
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(255, 243, 163));
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var cellText = values.Length > 0 ? values[0]?.ToString() : null;
        var keyword = values.Length > 1 ? values[1]?.ToString() : null;

        if (string.IsNullOrWhiteSpace(cellText) || string.IsNullOrWhiteSpace(keyword))
        {
            return TransparentBrush;
        }

        return cellText.Contains(keyword, StringComparison.OrdinalIgnoreCase) ? HighlightBrush : TransparentBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
