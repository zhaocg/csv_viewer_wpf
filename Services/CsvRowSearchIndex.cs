using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;

namespace CsvViewer.Services;

public sealed class CsvRowSearchIndex
{
    private static readonly ConditionalWeakTable<DataTable, CsvRowSearchIndex> Cache = new();
    private readonly string[] _rowTexts;

    private CsvRowSearchIndex(string[] rowTexts)
    {
        _rowTexts = rowTexts;
    }

    public static CsvRowSearchIndex Build(DataTable table)
    {
        return Cache.GetValue(table, BuildCore);
    }

    public SearchMatchResult FindPlainTextMatches(string keyword, bool isCaseSensitive)
    {
        var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new bool[_rowTexts.Length];
        var matchCount = 0;

        for (var rowIndex = 0; rowIndex < _rowTexts.Length; rowIndex++)
        {
            var isMatch = _rowTexts[rowIndex].IndexOf(keyword, comparison) >= 0;
            matches[rowIndex] = isMatch;
            if (isMatch)
            {
                matchCount++;
            }
        }

        return new SearchMatchResult(matches, matchCount);
    }

    private static CsvRowSearchIndex BuildCore(DataTable table)
    {
        var rowTexts = new string[table.Rows.Count];
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            rowTexts[rowIndex] = BuildRowText(table.Rows[rowIndex], table.Columns.Count);
        }

        return new CsvRowSearchIndex(rowTexts);
    }

    private static string BuildRowText(DataRow row, int columnCount)
    {
        var builder = new StringBuilder();
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (row.Table.Columns[columnIndex].ColumnName == CsvSearchFilter.MatchColumnName)
            {
                continue;
            }

            if (row.Table.Columns[columnIndex].ColumnName == CsvSearchFilter.ScrollableColumnName)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\0');
            }

            builder.Append(Convert.ToString(row[columnIndex]));
        }

        return builder.ToString();
    }
}
