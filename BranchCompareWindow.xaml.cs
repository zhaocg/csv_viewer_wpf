using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CsvViewer.Models;

namespace CsvViewer;

public partial class BranchCompareWindow : Window
{
    private readonly BranchCompareResult _result;
    private readonly CellDifferenceBrushConverter _leftConverter;
    private readonly CellDifferenceBrushConverter _rightConverter;
    private readonly List<CellPosition> _differencePositions;
    private IReadOnlyList<int> _visibleRows = [];
    private IReadOnlyList<int> _visibleColumns = [];
    private int _currentDifferenceIndex = -1;
    private bool _syncingScroll;
    private bool _syncingSelection;
    private bool _showOnlyDifferences;

    public BranchCompareWindow(BranchCompareResult result)
    {
        InitializeComponent();
        _result = result;
        _leftConverter = new CellDifferenceBrushConverter(this, result.LeftDifferences);
        _rightConverter = new CellDifferenceBrushConverter(this, result.RightDifferences);
        _differencePositions = result.LeftDifferences
            .Union(result.RightDifferences)
            .OrderBy(position => position.RowIndex)
            .ThenBy(position => position.ColumnIndex)
            .ToList();
        DataContext = result;
        RefreshDisplayedTables();
        UpdateNavigationState();
    }

    private void DataGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn textColumn || sender is not DataGrid dataGrid)
        {
            return;
        }

        var columnIndex = dataGrid.Columns.Count;
        textColumn.CellStyle = CreateCellStyle(columnIndex, dataGrid == LeftDataGrid ? _leftConverter : _rightConverter);
    }

    private static Style CreateCellStyle(int columnIndex, IValueConverter converter)
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
        style.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new Binding
        {
            Converter = converter,
            ConverterParameter = columnIndex
        }));

        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 99, 235))));
        selectedTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.White));
        style.Triggers.Add(selectedTrigger);

        return style;
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex() + 1;
    }

    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift || sender is not DataGrid dataGrid)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
        if (scrollViewer == null)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void PreviousDifference_Click(object sender, RoutedEventArgs e)
    {
        NavigateDifference(-1);
    }

    private void NextDifference_Click(object sender, RoutedEventArgs e)
    {
        NavigateDifference(1);
    }

    private void ShowOnlyDifferences_Changed(object sender, RoutedEventArgs e)
    {
        _showOnlyDifferences = sender is System.Windows.Controls.Primitives.ToggleButton { IsChecked: true };
        RefreshDisplayedTables();
        if (_currentDifferenceIndex >= 0 && _currentDifferenceIndex < _differencePositions.Count)
        {
            SelectDifference(_differencePositions[_currentDifferenceIndex]);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LeftDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncScroll(RightDataGrid, e);
    }

    private void RightDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncScroll(LeftDataGrid, e);
    }

    private void LeftDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        SyncSelectedCell(LeftDataGrid, RightDataGrid);
    }

    private void RightDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        SyncSelectedCell(RightDataGrid, LeftDataGrid);
    }

    private void SyncSelectedCell(DataGrid source, DataGrid target)
    {
        if (_syncingSelection || source.SelectedCells.Count == 0)
        {
            return;
        }

        var sourceCell = source.SelectedCells[0];
        var rowIndex = source.Items.IndexOf(sourceCell.Item);
        var columnIndex = sourceCell.Column?.DisplayIndex ?? -1;
        if (rowIndex < 0 || columnIndex < 0 || rowIndex >= target.Items.Count || columnIndex >= target.Columns.Count)
        {
            return;
        }

        try
        {
            _syncingSelection = true;
            var targetItem = target.Items[rowIndex];
            var targetColumn = target.Columns.FirstOrDefault(column => column.DisplayIndex == columnIndex);
            if (targetColumn == null)
            {
                return;
            }

            target.SelectedCells.Clear();
            target.CurrentCell = new DataGridCellInfo(targetItem, targetColumn);
            target.SelectedCells.Add(target.CurrentCell);
            target.ScrollIntoView(targetItem, targetColumn);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void NavigateDifference(int direction)
    {
        if (_differencePositions.Count == 0)
        {
            return;
        }

        _currentDifferenceIndex = _currentDifferenceIndex < 0
            ? (direction > 0 ? 0 : _differencePositions.Count - 1)
            : (_currentDifferenceIndex + direction + _differencePositions.Count) % _differencePositions.Count;

        SelectDifference(_differencePositions[_currentDifferenceIndex]);
    }

    private void SelectDifference(CellPosition position)
    {
        var displayRowIndex = IndexOf(_visibleRows, position.RowIndex);
        var displayColumnIndex = IndexOf(_visibleColumns, position.ColumnIndex);
        if (displayRowIndex < 0 || displayColumnIndex < 0)
        {
            return;
        }

        try
        {
            _syncingSelection = true;
            SelectCell(LeftDataGrid, displayRowIndex, displayColumnIndex);
            SelectCell(RightDataGrid, displayRowIndex, displayColumnIndex);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static void SelectCell(DataGrid dataGrid, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || rowIndex >= dataGrid.Items.Count || columnIndex >= dataGrid.Columns.Count)
        {
            return;
        }

        var item = dataGrid.Items[rowIndex];
        var column = dataGrid.Columns.FirstOrDefault(value => value.DisplayIndex == columnIndex);
        if (column == null)
        {
            return;
        }

        dataGrid.SelectedCells.Clear();
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.SelectedCells.Add(dataGrid.CurrentCell);
        dataGrid.ScrollIntoView(item, column);
    }

    private void RefreshDisplayedTables()
    {
        _visibleRows = GetVisibleRows();
        _visibleColumns = GetVisibleColumns();
        LeftDataGrid.ItemsSource = CreateDisplayTable(_result.LeftTable).DefaultView;
        RightDataGrid.ItemsSource = CreateDisplayTable(_result.RightTable).DefaultView;
    }

    private IReadOnlyList<int> GetVisibleRows()
    {
        if (_showOnlyDifferences)
        {
            return _differencePositions.Select(position => position.RowIndex).Distinct().Order().ToArray();
        }

        var rowCount = Math.Max(_result.LeftTable.Rows.Count, _result.RightTable.Rows.Count);
        return Enumerable.Range(0, rowCount).ToArray();
    }

    private IReadOnlyList<int> GetVisibleColumns()
    {
        if (_showOnlyDifferences)
        {
            return _differencePositions.Select(position => position.ColumnIndex).Distinct().Order().ToArray();
        }

        var columnCount = Math.Max(_result.LeftTable.Columns.Count, _result.RightTable.Columns.Count);
        return Enumerable.Range(0, columnCount).ToArray();
    }

    private DataTable CreateDisplayTable(DataTable source)
    {
        var table = new DataTable();
        foreach (var originalColumnIndex in _visibleColumns)
        {
            var columnName = originalColumnIndex < source.Columns.Count ? source.Columns[originalColumnIndex].ColumnName : GetExcelColumnName(originalColumnIndex);
            table.Columns.Add(MakeUniqueColumnName(table, columnName), typeof(string));
        }

        foreach (var originalRowIndex in _visibleRows)
        {
            var row = table.NewRow();
            for (var displayColumnIndex = 0; displayColumnIndex < _visibleColumns.Count; displayColumnIndex++)
            {
                var originalColumnIndex = _visibleColumns[displayColumnIndex];
                row[displayColumnIndex] = originalRowIndex < source.Rows.Count && originalColumnIndex < source.Columns.Count
                    ? Convert.ToString(source.Rows[originalRowIndex][originalColumnIndex]) ?? string.Empty
                    : string.Empty;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private void UpdateNavigationState()
    {
        var hasDifferences = _differencePositions.Count > 0;
        PreviousDifferenceButton.IsEnabled = hasDifferences;
        NextDifferenceButton.IsEnabled = hasDifferences;
    }

    private int GetOriginalRowIndex(int displayRowIndex)
    {
        return displayRowIndex >= 0 && displayRowIndex < _visibleRows.Count ? _visibleRows[displayRowIndex] : -1;
    }

    private int GetOriginalColumnIndex(int displayColumnIndex)
    {
        return displayColumnIndex >= 0 && displayColumnIndex < _visibleColumns.Count ? _visibleColumns[displayColumnIndex] : -1;
    }

    private static int IndexOf(IReadOnlyList<int> values, int value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
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

    private void SyncScroll(DataGrid target, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(target);
        if (scrollViewer == null)
        {
            return;
        }

        try
        {
            _syncingScroll = true;
            if (e.VerticalChange != 0)
            {
                scrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            }

            if (e.HorizontalChange != 0)
            {
                scrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private sealed class CellDifferenceBrushConverter(BranchCompareWindow owner, IReadOnlySet<CellPosition> differences) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DataRowView rowView || parameter is not int columnIndex)
            {
                return Brushes.Transparent;
            }

            var displayRowIndex = rowView.Row.Table.Rows.IndexOf(rowView.Row);
            var rowIndex = owner.GetOriginalRowIndex(displayRowIndex);
            var originalColumnIndex = owner.GetOriginalColumnIndex(columnIndex);
            return differences.Contains(new CellPosition(rowIndex, originalColumnIndex))
                ? new SolidColorBrush(Color.FromRgb(253, 224, 71))
                : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
