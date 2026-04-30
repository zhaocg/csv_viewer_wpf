using System.Data;
using CsvViewer.Models;

namespace CsvViewer.Services;

public sealed class CsvCompareService
{
    public BranchCompareResult Compare(
        DataTable leftSource,
        DataTable rightSource,
        string leftTitle,
        string rightTitle)
    {
        var rowAlignment = Align(
            GetRowSignatures(leftSource),
            GetRowSignatures(rightSource));
        var columnAlignment = Align(
            GetColumnSignatures(leftSource),
            GetColumnSignatures(rightSource));

        var leftTable = CreateDisplayTable(leftSource, rowAlignment, columnAlignment, leftSide: true);
        var rightTable = CreateDisplayTable(rightSource, rowAlignment, columnAlignment, leftSide: false);
        var leftDifferences = new HashSet<CellPosition>();
        var rightDifferences = new HashSet<CellPosition>();

        for (var displayRowIndex = 0; displayRowIndex < rowAlignment.Count; displayRowIndex++)
        {
            var (leftRowIndex, rightRowIndex) = rowAlignment[displayRowIndex];
            for (var displayColumnIndex = 0; displayColumnIndex < columnAlignment.Count; displayColumnIndex++)
            {
                var (leftColumnIndex, rightColumnIndex) = columnAlignment[displayColumnIndex];
                var leftExists = leftRowIndex.HasValue && leftColumnIndex.HasValue;
                var rightExists = rightRowIndex.HasValue && rightColumnIndex.HasValue;
                var leftValue = string.Empty;
                var rightValue = string.Empty;
                if (leftRowIndex is int leftRow && leftColumnIndex is int leftColumn)
                {
                    leftValue = Convert.ToString(leftSource.Rows[leftRow][leftColumn]) ?? string.Empty;
                }

                if (rightRowIndex is int rightRow && rightColumnIndex is int rightColumn)
                {
                    rightValue = Convert.ToString(rightSource.Rows[rightRow][rightColumn]) ?? string.Empty;
                }

                if (leftExists != rightExists || !string.Equals(leftValue, rightValue, StringComparison.Ordinal))
                {
                    if (leftExists)
                    {
                        leftDifferences.Add(new CellPosition(displayRowIndex, displayColumnIndex));
                    }

                    if (rightExists)
                    {
                        rightDifferences.Add(new CellPosition(displayRowIndex, displayColumnIndex));
                    }
                }
            }
        }

        return new BranchCompareResult
        {
            LeftTitle = leftTitle,
            RightTitle = rightTitle,
            LeftTable = leftTable,
            RightTable = rightTable,
            LeftDifferences = leftDifferences,
            RightDifferences = rightDifferences,
            SummaryText = $"差异单元格: {Math.Max(leftDifferences.Count, rightDifferences.Count):N0}，行数 {leftSource.Rows.Count:N0} / {rightSource.Rows.Count:N0}，列数 {leftSource.Columns.Count:N0} / {rightSource.Columns.Count:N0}"
        };
    }

    private static DataTable CreateDisplayTable(
        DataTable source,
        IReadOnlyList<AlignedIndex> rowAlignment,
        IReadOnlyList<AlignedIndex> columnAlignment,
        bool leftSide)
    {
        var table = new DataTable();
        for (var displayColumnIndex = 0; displayColumnIndex < columnAlignment.Count; displayColumnIndex++)
        {
            var sourceColumnIndex = leftSide ? columnAlignment[displayColumnIndex].LeftIndex : columnAlignment[displayColumnIndex].RightIndex;
            var fallbackColumnIndex = (leftSide ? columnAlignment[displayColumnIndex].RightIndex : columnAlignment[displayColumnIndex].LeftIndex) ?? displayColumnIndex;
            var columnName = sourceColumnIndex.HasValue && sourceColumnIndex.Value < source.Columns.Count
                ? source.Columns[sourceColumnIndex.Value].ColumnName
                : GetExcelColumnName(fallbackColumnIndex);
            table.Columns.Add(MakeUniqueColumnName(table, columnName), typeof(string));
        }

        for (var displayRowIndex = 0; displayRowIndex < rowAlignment.Count; displayRowIndex++)
        {
            var sourceRowIndex = leftSide ? rowAlignment[displayRowIndex].LeftIndex : rowAlignment[displayRowIndex].RightIndex;
            var row = table.NewRow();
            for (var displayColumnIndex = 0; displayColumnIndex < columnAlignment.Count; displayColumnIndex++)
            {
                var sourceColumnIndex = leftSide ? columnAlignment[displayColumnIndex].LeftIndex : columnAlignment[displayColumnIndex].RightIndex;
                row[displayColumnIndex] = sourceRowIndex.HasValue && sourceColumnIndex.HasValue
                    ? Convert.ToString(source.Rows[sourceRowIndex.Value][sourceColumnIndex.Value]) ?? string.Empty
                    : string.Empty;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static IReadOnlyList<string> GetRowSignatures(DataTable table)
    {
        return table.Rows.Cast<DataRow>()
            .Select(row => string.Join('\u001F', row.ItemArray.Select(value => Convert.ToString(value) ?? string.Empty)))
            .ToArray();
    }

    private static IReadOnlyList<string> GetColumnSignatures(DataTable table)
    {
        var signatures = new List<string>();
        for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var values = new List<string> { table.Columns[columnIndex].ColumnName };
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                values.Add(Convert.ToString(table.Rows[rowIndex][columnIndex]) ?? string.Empty);
            }

            signatures.Add(string.Join('\u001F', values));
        }

        return signatures;
    }

    private static IReadOnlyList<AlignedIndex> Align(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var lengths = new int[left.Count + 1, right.Count + 1];
        for (var leftIndex = left.Count - 1; leftIndex >= 0; leftIndex--)
        {
            for (var rightIndex = right.Count - 1; rightIndex >= 0; rightIndex--)
            {
                lengths[leftIndex, rightIndex] = string.Equals(left[leftIndex], right[rightIndex], StringComparison.Ordinal)
                    ? lengths[leftIndex + 1, rightIndex + 1] + 1
                    : Math.Max(lengths[leftIndex + 1, rightIndex], lengths[leftIndex, rightIndex + 1]);
            }
        }

        var aligned = new List<AlignedIndex>();
        var i = 0;
        var j = 0;
        while (i < left.Count && j < right.Count)
        {
            if (string.Equals(left[i], right[j], StringComparison.Ordinal))
            {
                aligned.Add(new AlignedIndex(i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                aligned.Add(new AlignedIndex(i, null));
                i++;
            }
            else
            {
                aligned.Add(new AlignedIndex(null, j));
                j++;
            }
        }

        while (i < left.Count)
        {
            aligned.Add(new AlignedIndex(i, null));
            i++;
        }

        while (j < right.Count)
        {
            aligned.Add(new AlignedIndex(null, j));
            j++;
        }

        return aligned;
    }

    private static string GetExcelColumnName(int zeroBasedIndex)
    {
        var dividend = zeroBasedIndex + 1;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = (char)('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string MakeUniqueColumnName(DataTable table, string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "Column" : baseName;
        var index = 2;
        while (table.Columns.Contains(name))
        {
            name = $"{baseName} ({index++})";
        }

        return name;
    }

    private readonly record struct AlignedIndex(int? LeftIndex, int? RightIndex);
}
