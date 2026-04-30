using System.Collections.Generic;
using System.Data;

namespace CsvViewer.Models;

public sealed class BranchCompareResult
{
    public required string LeftTitle { get; init; }
    public required string RightTitle { get; init; }
    public required DataTable LeftTable { get; init; }
    public required DataTable RightTable { get; init; }
    public required IReadOnlySet<CellPosition> LeftDifferences { get; init; }
    public required IReadOnlySet<CellPosition> RightDifferences { get; init; }
    public required string SummaryText { get; init; }
}

public readonly record struct CellPosition(int RowIndex, int ColumnIndex);
