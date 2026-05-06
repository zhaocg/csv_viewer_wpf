using System;
using System.Data;

namespace CsvViewer.Services;

public static class CsvSearchFilter
{
    public const string MatchColumnName = "__CsvViewerSearchMatch";

    public static DataTable Filter(DataTable table, SearchMatcher matcher, bool isWholeWord)
    {
        var filtered = table.Clone();
        foreach (DataRow row in table.Rows)
        {
            if (IsMatch(row, table.Columns.Count, matcher, isWholeWord))
            {
                filtered.ImportRow(row);
            }
        }

        return filtered;
    }

    public static SearchFilterResult ApplyFilterView(DataTable table, SearchMatcher matcher, bool isWholeWord)
    {
        return ApplyFilterView(table, FindMatches(table, matcher, isWholeWord));
    }

    public static SearchMatchResult FindMatches(DataTable table, SearchMatcher matcher, bool isWholeWord)
    {
        var matches = new bool[table.Rows.Count];
        var matchCount = 0;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var isMatch = IsMatch(table.Rows[rowIndex], table.Columns.Count, matcher, isWholeWord);
            matches[rowIndex] = isMatch;
            if (isMatch)
            {
                matchCount++;
            }
        }

        return new SearchMatchResult(matches, matchCount);
    }

    public static SearchFilterResult ApplyFilterView(DataTable table, SearchMatchResult matches)
    {
        var matchColumn = EnsureMatchColumn(table);

        table.BeginLoadData();
        try
        {
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                table.Rows[rowIndex][matchColumn] = rowIndex < matches.Matches.Length && matches.Matches[rowIndex];
            }
        }
        finally
        {
            table.EndLoadData();
        }

        var view = table.DefaultView;
        view.RowFilter = $"[{MatchColumnName}] = true";
        return new SearchFilterResult(view, matches.MatchCount);
    }

    public static void ClearFilterView(DataTable table)
    {
        table.DefaultView.RowFilter = string.Empty;
    }

    private static bool IsMatch(DataRow row, int columnCount, SearchMatcher matcher, bool isWholeWord)
    {
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (row.Table.Columns[columnIndex].ColumnName == MatchColumnName)
            {
                continue;
            }

            if (matcher.IsMatch(Convert.ToString(row[columnIndex]), isWholeWord))
            {
                return true;
            }
        }

        return false;
    }

    private static DataColumn EnsureMatchColumn(DataTable table)
    {
        if (table.Columns[MatchColumnName] is DataColumn existingColumn)
        {
            return existingColumn;
        }

        var column = new DataColumn(MatchColumnName, typeof(bool))
        {
            ColumnMapping = MappingType.Hidden,
            DefaultValue = false
        };
        table.Columns.Add(column);
        return column;
    }
}

public sealed record SearchMatchResult(bool[] Matches, int MatchCount);

public sealed record SearchFilterResult(DataView View, int MatchCount);
