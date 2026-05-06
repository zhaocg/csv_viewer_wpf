using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CsvViewer.Services;

namespace CsvViewer.Converters;

public sealed class SearchHighlightForegroundConverter : IMultiValueConverter
{
    private readonly SearchMatcherCache _matcherCache = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var cellText = values.Length > 0 ? values[0]?.ToString() : null;
        var keyword = values.Length > 1 ? values[1]?.ToString() : null;
        var isCaseSensitive = values.Length > 2 && values[2] is true;
        var isWholeWord = values.Length > 3 && values[3] is true;
        var isRegex = values.Length > 4 && values[4] is true;
        var fallback = values.Length > 5 && values[5] is Brush brush ? brush : Brushes.Black;

        if (string.IsNullOrWhiteSpace(cellText) || string.IsNullOrWhiteSpace(keyword))
        {
            return fallback;
        }

        SearchMatcher matcher;
        try
        {
            matcher = _matcherCache.Get(keyword, isCaseSensitive, isWholeWord, isRegex);
        }
        catch (ArgumentException)
        {
            return fallback;
        }

        return matcher.IsMatch(cellText, isWholeWord) ? Brushes.Black : fallback;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
