namespace CsvViewer.Services;

public sealed class SearchMatcherCache
{
    private SearchMatcher? _matcher;
    private SearchMatcherKey _key;

    public SearchMatcher Get(string keyword, bool isCaseSensitive, bool isWholeWord, bool isRegex)
    {
        var key = new SearchMatcherKey(keyword, isCaseSensitive, isWholeWord, isRegex);
        if (_matcher != null && _key.Equals(key))
        {
            return _matcher;
        }

        _matcher = SearchMatcher.Create(keyword, isCaseSensitive, isWholeWord, isRegex);
        _key = key;
        return _matcher;
    }

    private readonly record struct SearchMatcherKey(string Keyword, bool IsCaseSensitive, bool IsWholeWord, bool IsRegex);
}
