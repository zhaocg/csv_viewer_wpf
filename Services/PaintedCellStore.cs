using System.Windows.Media;

namespace CsvViewer.Services;

public sealed class PaintedCellStore
{
    private readonly Dictionary<PaintedCellKey, Brush> _brushes = [];

    public void Clear()
    {
        _brushes.Clear();
    }

    public void Set(int rowIndex, string columnName, Brush brush)
    {
        _brushes[new PaintedCellKey(rowIndex, columnName)] = brush;
    }

    public bool TryGetBrush(Func<int> rowIndexResolver, string columnName, out Brush brush)
    {
        if (_brushes.Count == 0)
        {
            brush = Brushes.Transparent;
            return false;
        }

        var rowIndex = rowIndexResolver();
        if (rowIndex >= 0 && _brushes.TryGetValue(new PaintedCellKey(rowIndex, columnName), out brush!))
        {
            return true;
        }

        brush = Brushes.Transparent;
        return false;
    }

    private readonly record struct PaintedCellKey(int RowIndex, string ColumnName);
}
