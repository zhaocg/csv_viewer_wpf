using CsvViewer.Converters;
using CsvViewer.Services;
using System.Data;
using System.Globalization;
using System.Windows.Media;

var arguments = TortoiseSvnDiffLauncher.BuildDiffArguments(
    @"C:\Temp\CsvViewer\Compare\main\file.csv",
    @"C:\Temp\CsvViewer\Compare\dev\file.csv");

AssertEqual("/command:diff /path:\"C:\\Temp\\CsvViewer\\Compare\\main\\file.csv\" /path2:\"C:\\Temp\\CsvViewer\\Compare\\dev\\file.csv\"", arguments);
AssertThrows<ArgumentException>(() => TortoiseSvnDiffLauncher.BuildDiffArguments(
    "svn://server/branches/main/data/Excel/file.csv",
    @"C:\Temp\CsvViewer\Compare\dev\file.csv"));

var tortoisePathRoot = Path.Combine(Path.GetTempPath(), "CsvViewerTests", Guid.NewGuid().ToString("N"), "TortoiseSVN", "bin");
Directory.CreateDirectory(tortoisePathRoot);
var tortoiseProcPath = Path.Combine(tortoisePathRoot, "TortoiseProc.exe");
File.WriteAllText(tortoiseProcPath, string.Empty);
AssertEqual(tortoiseProcPath, TortoiseSvnDiffLauncher.FindTortoiseProcPath(tortoisePathRoot, string.Empty, string.Empty) ?? string.Empty);
AssertEqual(string.Empty, TortoiseSvnDiffLauncher.FindTortoiseProcPath(string.Empty, string.Empty, string.Empty) ?? string.Empty);

var cacheRoot = Path.Combine(Path.GetTempPath(), "CsvViewerTests", Guid.NewGuid().ToString("N"), "SvnFileCache");
var oldSession = Path.Combine(cacheRoot, "old-session");
var currentSession = Path.Combine(cacheRoot, "current-session");
Directory.CreateDirectory(oldSession);
Directory.CreateDirectory(currentSession);
File.WriteAllText(Path.Combine(oldSession, "old.csv"), "old");
File.WriteAllText(Path.Combine(currentSession, "current.csv"), "current");

SvnFolderService.CleanupOldSessionCaches(cacheRoot, "current-session");
AssertFalse(Directory.Exists(oldSession), "Old session cache should be removed on startup.");
AssertTrue(Directory.Exists(currentSession), "Current session cache should not be removed on startup.");

SvnFolderService.CleanupSessionCache(cacheRoot, "current-session");
AssertFalse(Directory.Exists(currentSession), "Current session cache should be removed on exit.");

var matcherCache = new SearchMatcherCache();
var cachedMatcher = matcherCache.Get("alpha", isCaseSensitive: false, isWholeWord: false, isRegex: false);
AssertSame(cachedMatcher, matcherCache.Get("alpha", isCaseSensitive: false, isWholeWord: false, isRegex: false));
AssertNotSame(cachedMatcher, matcherCache.Get("alpha", isCaseSensitive: true, isWholeWord: false, isRegex: false));

var table = new DataTable();
table.Columns.Add("Name");
table.Columns.Add("Value");
table.Rows.Add("alpha", "one");
table.Rows.Add("beta", "two");
table.Rows.Add("gamma", "alphabet");

var filtered = CsvSearchFilter.Filter(table, SearchMatcher.Create("alpha", isCaseSensitive: false, isWholeWord: false, isRegex: false), isWholeWord: false);
AssertEqual("2", filtered.Rows.Count.ToString());
AssertEqual("alpha", filtered.Rows[0]["Name"]?.ToString() ?? string.Empty);
AssertEqual("gamma", filtered.Rows[1]["Name"]?.ToString() ?? string.Empty);

var filterResult = CsvSearchFilter.ApplyFilterView(table, SearchMatcher.Create("alpha", isCaseSensitive: false, isWholeWord: false, isRegex: false), isWholeWord: false);
AssertSame(table, filterResult.View.Table!);
AssertEqual("2", filterResult.MatchCount.ToString());
AssertEqual("2", filterResult.View.Count.ToString());
AssertEqual("alpha", filterResult.View[0]["Name"]?.ToString() ?? string.Empty);
AssertEqual("gamma", filterResult.View[1]["Name"]?.ToString() ?? string.Empty);
AssertEqual("Hidden", table.Columns[CsvSearchFilter.MatchColumnName]?.ColumnMapping.ToString() ?? string.Empty);

var rowSearchIndex = CsvRowSearchIndex.Build(table);
var cachedMatches = rowSearchIndex.FindPlainTextMatches("ALPHA", isCaseSensitive: false);
AssertEqual("2", cachedMatches.MatchCount.ToString());
AssertTrue(cachedMatches.Matches[0], "First row should match cached case-insensitive search.");
AssertFalse(cachedMatches.Matches[1], "Second row should not match cached case-insensitive search.");
AssertTrue(cachedMatches.Matches[2], "Third row should match cached case-insensitive search.");
AssertSame(rowSearchIndex, CsvRowSearchIndex.Build(table));

var frozenFilterResult = CsvSearchFilter.ApplyFilterView(table, cachedMatches, frozenRowCount: 1);
AssertEqual("1", frozenFilterResult.MatchCount.ToString());
AssertEqual("1", frozenFilterResult.View.Count.ToString());
AssertEqual("gamma", frozenFilterResult.View[0]["Name"]?.ToString() ?? string.Empty);

var paintedCells = new PaintedCellStore();
var rowIndexLookups = 0;
AssertFalse(paintedCells.TryGetBrush(() =>
{
    rowIndexLookups++;
    return 0;
}, "Name", out _), "Empty painted cell store should not report a painted cell.");
AssertEqual("0", rowIndexLookups.ToString());

var paintBrush = Brushes.Yellow;
paintedCells.Set(2, "Name", paintBrush);
AssertTrue(paintedCells.TryGetBrush(() =>
{
    rowIndexLookups++;
    return 2;
}, "Name", out var resolvedBrush), "Painted cell should be found after it is stored.");
AssertEqual("1", rowIndexLookups.ToString());
AssertSame(paintBrush, resolvedBrush);

var textReads = 0;
var lazyText = new CountingText(() => textReads++);
var highlightBrush = new SearchHighlightBrushConverter().Convert([lazyText, string.Empty], typeof(Brush), null!, CultureInfo.InvariantCulture);
AssertSame(Brushes.Transparent, highlightBrush);
var highlightForeground = new SearchHighlightForegroundConverter().Convert([lazyText, string.Empty, false, false, false, Brushes.Red], typeof(Brush), null!, CultureInfo.InvariantCulture);
AssertSame(Brushes.Red, highlightForeground);
AssertEqual("0", textReads.ToString());

static void AssertEqual(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertSame(object expected, object actual)
{
    if (!ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException("Expected the same object instance.");
    }
}

static void AssertNotSame(object expected, object actual)
{
    if (ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException("Expected different object instances.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class CountingText(Action onToString)
{
    public override string ToString()
    {
        onToString();
        return "alpha";
    }
}
