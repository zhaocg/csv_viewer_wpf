using System;
using System.Text.RegularExpressions;

namespace CsvViewer.Services;

public sealed class SearchMatcher
{
    private readonly Regex? _regex;
    private readonly string _keyword;
    private readonly StringComparison _comparison;

    private SearchMatcher(string keyword, bool isCaseSensitive, Regex? regex)
    {
        _keyword = keyword;
        _comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _regex = regex;
    }

    public static SearchMatcher Create(string keyword, bool isCaseSensitive, bool isWholeWord, bool isRegex)
    {
        if (isRegex)
        {
            var pattern = isWholeWord ? $@"\b(?:{keyword})\b" : keyword;
            var options = isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return new SearchMatcher(keyword, isCaseSensitive, new Regex(pattern, options | RegexOptions.CultureInvariant));
        }

        return new SearchMatcher(keyword, isCaseSensitive, null);
    }

    public bool IsMatch(string? text, bool isWholeWord)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (_regex != null)
        {
            return _regex.IsMatch(text);
        }

        if (!isWholeWord)
        {
            return text.Contains(_keyword, _comparison);
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(_keyword, startIndex, _comparison);
            if (matchIndex < 0)
            {
                return false;
            }

            var endIndex = matchIndex + _keyword.Length;
            if (IsWordBoundary(text, matchIndex - 1) && IsWordBoundary(text, endIndex))
            {
                return true;
            }

            startIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool IsWordBoundary(string text, int index)
    {
        return index < 0 || index >= text.Length || !IsWordChar(text[index]);
    }

    private static bool IsWordChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
