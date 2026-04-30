using CsvViewer.Services;
using CsvViewer.ViewModels;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CsvViewer;

public partial class CsvDocumentGrid : UserControl
{
    public static readonly DependencyProperty CellPaintVersionProperty = DependencyProperty.Register(
        nameof(CellPaintVersion),
        typeof(int),
        typeof(CsvDocumentGrid),
        new PropertyMetadata(0));

    public static readonly DependencyProperty IsCellPaintedProperty = DependencyProperty.RegisterAttached(
        "IsCellPainted",
        typeof(bool),
        typeof(CsvDocumentGrid),
        new PropertyMetadata(false));

    private readonly ClipboardService _clipboardService = new();
    private readonly List<DataGridColumn> _mainWidthObservedColumns = [];
    private readonly List<DataGridColumn> _frozenWidthObservedColumns = [];
    private readonly Dictionary<PaintedCellKey, Brush> _paintedCellBrushes = [];
    private readonly CellPaintBrushConverter _cellPaintBrushConverter = new();
    private readonly Style _searchHighlightTextStyle;
    private IReadOnlyList<double>? _pendingColumnWidths;
    private bool _syncingHorizontalScroll;
    private bool _syncingColumnWidths;
    private bool _syncingSelection;
    private bool _columnWidthRestoreQueued;

    private static readonly Brush PaintedCellBrush = CreateFrozenBrush(Color.FromRgb(253, 224, 71));

    public int CellPaintVersion
    {
        get => (int)GetValue(CellPaintVersionProperty);
        set => SetValue(CellPaintVersionProperty, value);
    }

    public static void SetIsCellPainted(DependencyObject element, bool value)
    {
        element.SetValue(IsCellPaintedProperty, value);
    }

    public static bool GetIsCellPainted(DependencyObject element)
    {
        return (bool)element.GetValue(IsCellPaintedProperty);
    }

    public CsvDocumentGrid()
    {
        InitializeComponent();
        _searchHighlightTextStyle = CreateSearchHighlightTextStyle();
        DataContextChanged += CsvDocumentGrid_DataContextChanged;
        Loaded += CsvDocumentGrid_Loaded;
    }

    public bool CopySelectedCells()
    {
        if (FrozenRowsDataGrid.SelectedCells.Count > 0)
        {
            return _clipboardService.CopySelectedCells(FrozenRowsDataGrid);
        }

        return _clipboardService.CopySelectedCells(CsvDataGrid);
    }

    public bool PaintSelectedCells()
    {
        var painted = PaintSelectedCells(FrozenRowsDataGrid) | PaintSelectedCells(CsvDataGrid);
        if (painted)
        {
            CellPaintVersion++;
        }

        return painted;
    }

    public bool FreezeToCurrentCell()
    {
        var rowCount = GetCurrentCellRowFreezeCount();
        var columnCount = GetCurrentCellColumnFreezeCount();
        if (rowCount == 0 && columnCount == 0)
        {
            return false;
        }

        if (DataContext is CsvDocumentViewModel document)
        {
            BeginColumnWidthRestore();
            document.SetFrozenRowCount(rowCount);
            document.FrozenColumnCount = columnCount;
        }

        ApplyFrozenColumnCount(columnCount);
        RestorePendingColumnWidths();
        QueuePendingColumnWidthRestore();
        return true;
    }

    public void ClearFreeze()
    {
        if (DataContext is CsvDocumentViewModel document)
        {
            BeginColumnWidthRestore();
            document.SetFrozenRowCount(0);
            document.FrozenColumnCount = 0;
        }

        ApplyFrozenColumnCount(0);
        RestorePendingColumnWidths();
        QueuePendingColumnWidthRestore();
    }

    public void ApplyDocumentFrozenState()
    {
        ApplyFrozenColumnCount(GetFrozenColumnCount());
    }

    private void CsvDocumentGrid_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _paintedCellBrushes.Clear();
        CellPaintVersion++;
        ApplyDocumentFrozenState();
        QueueColumnLayoutSync();
    }

    private void CsvDocumentGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySelectAllButtonTheme(CsvDataGrid);
        ApplySelectAllButtonTheme(FrozenRowsDataGrid);
    }

    private void CsvDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex() + GetFrozenRowCount() + 1;
    }

    private void FrozenRowsDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex() + 1;
    }

    private void FrozenRowsDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        ClearOtherGridSelection(FrozenRowsDataGrid, CsvDataGrid);
    }

    private void CsvDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        ClearOtherGridSelection(CsvDataGrid, FrozenRowsDataGrid);
    }

    private void ClearOtherGridSelection(DataGrid source, DataGrid target)
    {
        if (_syncingSelection || source.SelectedCells.Count == 0 || target.SelectedCells.Count == 0)
        {
            return;
        }

        try
        {
            _syncingSelection = true;
            target.SelectedCells.Clear();
            target.UnselectAll();
            target.CurrentCell = default;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void DataGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is DataGridTextColumn textColumn)
        {
            textColumn.SortMemberPath = e.PropertyName;
            textColumn.CellStyle = CreateCellPaintStyle(e.PropertyName);
            textColumn.ElementStyle = _searchHighlightTextStyle;
        }
    }

    private void CsvDataGrid_AutoGeneratedColumns(object? sender, EventArgs e)
    {
        ApplyDocumentFrozenState();
        RestorePendingColumnWidths();
        RegisterColumnWidthSync();
        QueueColumnLayoutSync();
        ApplySelectAllButtonTheme(CsvDataGrid);
    }

    private void FrozenRowsDataGrid_AutoGeneratedColumns(object? sender, EventArgs e)
    {
        ApplyDocumentFrozenState();
        RestorePendingColumnWidths();
        RegisterColumnWidthSync();
        QueueColumnLayoutSync();
        ApplySelectAllButtonTheme(FrozenRowsDataGrid);
    }

    private int GetFrozenRowCount()
    {
        return DataContext is CsvDocumentViewModel document ? document.FrozenRowCount : 0;
    }

    private int GetFrozenColumnCount()
    {
        return DataContext is CsvDocumentViewModel document ? document.FrozenColumnCount : 0;
    }

    private void ApplyFrozenColumnCount(int count)
    {
        SetFrozenColumnCount(CsvDataGrid, count);
        SetFrozenColumnCount(FrozenRowsDataGrid, count);
    }

    private static void SetFrozenColumnCount(DataGrid dataGrid, int count)
    {
        var frozenColumnCount = Math.Clamp(count, 0, dataGrid.Columns.Count);
        if (dataGrid.FrozenColumnCount != frozenColumnCount)
        {
            dataGrid.FrozenColumnCount = frozenColumnCount;
        }
    }

    private int GetCurrentCellRowFreezeCount()
    {
        if (TryGetActiveCell(out var dataGrid, out _, out var item))
        {
            var rowIndex = dataGrid.Items.IndexOf(item);
            if (rowIndex < 0)
            {
                return 0;
            }

            return dataGrid == FrozenRowsDataGrid ? rowIndex : GetFrozenRowCount() + rowIndex;
        }

        return 0;
    }

    private int GetCurrentCellColumnFreezeCount()
    {
        return TryGetActiveCell(out _, out var column, out _) ? column.DisplayIndex : 0;
    }

    private bool TryGetActiveCell(out DataGrid dataGrid, out DataGridColumn column, out object item)
    {
        if (FrozenRowsDataGrid.IsKeyboardFocusWithin && TryGetCurrentCell(FrozenRowsDataGrid, out column!, out item!))
        {
            dataGrid = FrozenRowsDataGrid;
            return true;
        }

        if (CsvDataGrid.IsKeyboardFocusWithin && TryGetCurrentCell(CsvDataGrid, out column!, out item!))
        {
            dataGrid = CsvDataGrid;
            return true;
        }

        if (TryGetCurrentCell(FrozenRowsDataGrid, out column!, out item!))
        {
            dataGrid = FrozenRowsDataGrid;
            return true;
        }

        if (TryGetCurrentCell(CsvDataGrid, out column!, out item!))
        {
            dataGrid = CsvDataGrid;
            return true;
        }

        dataGrid = CsvDataGrid;
        column = null!;
        item = null!;
        return false;
    }

    private static bool TryGetCurrentCell(DataGrid dataGrid, out DataGridColumn column, out object item)
    {
        if (dataGrid.SelectedCells.Count > 0)
        {
            var selectedCell = dataGrid.SelectedCells[0];
            if (selectedCell.Column != null && selectedCell.Item != null)
            {
                column = selectedCell.Column;
                item = selectedCell.Item;
                return true;
            }
        }

        if (dataGrid.CurrentColumn != null && dataGrid.CurrentItem != null)
        {
            column = dataGrid.CurrentColumn;
            item = dataGrid.CurrentItem;
            return true;
        }

        column = null!;
        item = null!;
        return false;
    }

    private void CsvDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncHorizontalScroll(FrozenRowsDataGrid, e.HorizontalOffset, e.HorizontalChange);
    }

    private void CsvDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(CsvDataGrid);
        if (scrollViewer == null)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void FrozenRowsDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncHorizontalScroll(CsvDataGrid, e.HorizontalOffset, e.HorizontalChange);
    }

    private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindVisualParent<Thumb>(source) != null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            var rowHeader = FindVisualParent<DataGridRowHeader>(source);
            if (rowHeader != null && FindVisualParent<DataGrid>(rowHeader) is DataGrid rowHeaderDataGrid)
            {
                SelectRowAndFitColumns(rowHeaderDataGrid, rowHeader.DataContext);
                e.Handled = true;
                return;
            }
        }

        var header = FindVisualParent<DataGridColumnHeader>(source);
        if (header?.Column == null)
        {
            return;
        }

        var columnName = GetColumnName(header.Column);
        if (string.IsNullOrEmpty(columnName))
        {
            return;
        }

        SelectWholeColumn(columnName);
        e.Handled = true;
    }

    private void SelectRowAndFitColumns(DataGrid dataGrid, object? rowItem)
    {
        if (rowItem is not DataRowView rowView)
        {
            return;
        }

        dataGrid.SelectedCells.Clear();
        var currentCellSet = false;
        foreach (var column in dataGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            if (!currentCellSet)
            {
                dataGrid.CurrentCell = new DataGridCellInfo(rowItem, column);
                currentCellSet = true;
            }

            dataGrid.SelectedCells.Add(new DataGridCellInfo(rowItem, column));
        }

        FitColumnsToRow(dataGrid, rowView);
    }

    private void FitColumnsToRow(DataGrid sourceGrid, DataRowView rowView)
    {
        var widths = new List<double>(sourceGrid.Columns.Count);
        foreach (var column in sourceGrid.Columns)
        {
            var columnName = GetColumnName(column);
            widths.Add(string.IsNullOrEmpty(columnName) ? column.ActualWidth : MeasureCellWidth(sourceGrid, rowView, columnName));
        }

        ApplyColumnWidths(CsvDataGrid, widths);
        ApplyColumnWidths(FrozenRowsDataGrid, widths);
    }

    private double MeasureCellWidth(DataGrid dataGrid, DataRowView rowView, string columnName)
    {
        var text = rowView.Row.Table.Columns.Contains(columnName) ? Convert.ToString(rowView[columnName]) ?? string.Empty : string.Empty;
        var formattedText = new FormattedText(
            text.Length == 0 ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(dataGrid.FontFamily, dataGrid.FontStyle, dataGrid.FontWeight, dataGrid.FontStretch),
            dataGrid.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return Math.Max(48, Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace) + 24);
    }

    private void SelectWholeColumn(string columnName)
    {
        FrozenRowsDataGrid.SelectedCells.Clear();
        CsvDataGrid.SelectedCells.Clear();

        SelectColumnCells(FrozenRowsDataGrid, columnName);
        SelectColumnCells(CsvDataGrid, columnName);
        CsvDataGrid.Focus();
    }

    private static void SelectColumnCells(DataGrid dataGrid, string columnName)
    {
        var column = FindColumn(dataGrid, columnName);
        if (column == null)
        {
            return;
        }

        var currentCellSet = false;
        foreach (var item in dataGrid.Items)
        {
            if (item == CollectionView.NewItemPlaceholder)
            {
                continue;
            }

            if (!currentCellSet)
            {
                dataGrid.CurrentCell = new DataGridCellInfo(item, column);
                currentCellSet = true;
            }

            dataGrid.SelectedCells.Add(new DataGridCellInfo(item, column));
        }
    }

    private bool PaintSelectedCells(DataGrid dataGrid)
    {
        var painted = false;
        foreach (var selectedCell in dataGrid.SelectedCells.ToList())
        {
            if (selectedCell.Item is not DataRowView rowView || selectedCell.Column == null)
            {
                continue;
            }

            var rowIndex = GetVisualRowIndex(dataGrid, rowView);
            var columnName = GetColumnName(selectedCell.Column);
            if (rowIndex < 0 || string.IsNullOrEmpty(columnName))
            {
                continue;
            }

            _paintedCellBrushes[new PaintedCellKey(rowIndex, columnName)] = PaintedCellBrush;
            painted = true;
        }

        return painted;
    }

    private bool TryGetCellPaintBrush(DataGrid dataGrid, DataRowView rowView, string columnName, out Brush brush)
    {
        var rowIndex = GetVisualRowIndex(dataGrid, rowView);
        if (rowIndex >= 0 && _paintedCellBrushes.TryGetValue(new PaintedCellKey(rowIndex, columnName), out brush!))
        {
            return true;
        }

        brush = Brushes.Transparent;
        return false;
    }

    private int GetVisualRowIndex(DataGrid dataGrid, DataRowView rowView)
    {
        var rowIndex = rowView.Row.Table.Rows.IndexOf(rowView.Row);
        if (rowIndex < 0)
        {
            return -1;
        }

        return dataGrid == CsvDataGrid ? rowIndex + GetFrozenRowCount() : rowIndex;
    }

    private void BeginColumnWidthRestore()
    {
        var widths = CaptureColumnWidths();
        if (widths.Count > 0)
        {
            _pendingColumnWidths = widths;
        }
    }

    private IReadOnlyList<double> CaptureColumnWidths()
    {
        var source = CsvDataGrid.Columns.Count > 0 ? CsvDataGrid : FrozenRowsDataGrid;
        var widths = new List<double>(source.Columns.Count);

        foreach (var column in source.Columns)
        {
            var width = column.ActualWidth;
            if (double.IsNaN(width) || width <= 0)
            {
                width = column.Width.DisplayValue;
            }

            widths.Add(!double.IsNaN(width) && width > 0 ? width : 0);
        }

        return widths;
    }

    private void RestorePendingColumnWidths()
    {
        if (_pendingColumnWidths is not { Count: > 0 } widths)
        {
            return;
        }

        try
        {
            _syncingColumnWidths = true;
            ApplyColumnWidths(CsvDataGrid, widths);
            ApplyColumnWidths(FrozenRowsDataGrid, widths);
        }
        finally
        {
            _syncingColumnWidths = false;
        }
    }

    private void QueuePendingColumnWidthRestore()
    {
        if (_pendingColumnWidths == null || _columnWidthRestoreQueued)
        {
            return;
        }

        _columnWidthRestoreQueued = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            RestorePendingColumnWidths();
            _pendingColumnWidths = null;
            _columnWidthRestoreQueued = false;
            QueueColumnLayoutSync();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void ApplyColumnWidths(DataGrid dataGrid, IReadOnlyList<double> widths)
    {
        var count = Math.Min(widths.Count, dataGrid.Columns.Count);
        for (var i = 0; i < count; i++)
        {
            if (widths[i] > 0)
            {
                dataGrid.Columns[i].Width = new DataGridLength(widths[i]);
            }
        }
    }

    private void RegisterColumnWidthSync()
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
        foreach (var column in _mainWidthObservedColumns)
        {
            descriptor.RemoveValueChanged(column, MainColumnWidthChanged);
        }

        foreach (var column in _frozenWidthObservedColumns)
        {
            descriptor.RemoveValueChanged(column, FrozenColumnWidthChanged);
        }

        _mainWidthObservedColumns.Clear();
        _frozenWidthObservedColumns.Clear();

        foreach (var column in CsvDataGrid.Columns)
        {
            descriptor.AddValueChanged(column, MainColumnWidthChanged);
            _mainWidthObservedColumns.Add(column);
        }

        foreach (var column in FrozenRowsDataGrid.Columns)
        {
            descriptor.AddValueChanged(column, FrozenColumnWidthChanged);
            _frozenWidthObservedColumns.Add(column);
        }
    }

    private void MainColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_syncingColumnWidths)
        {
            return;
        }

        SyncColumnLayouts(CsvDataGrid, FrozenRowsDataGrid);
    }

    private void FrozenColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_syncingColumnWidths)
        {
            return;
        }

        SyncColumnLayouts(FrozenRowsDataGrid, CsvDataGrid);
    }

    private void QueueColumnLayoutSync()
    {
        Dispatcher.BeginInvoke(() =>
        {
            SyncColumnLayouts(CsvDataGrid, FrozenRowsDataGrid);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void ApplySelectAllButtonTheme(DataGrid dataGrid)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (FindVisualChild<Button>(dataGrid) is not Button button)
            {
                return;
            }

            button.SetResourceReference(Control.BackgroundProperty, "DataGridHeaderBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "GridLineBrush");
            button.SetResourceReference(Control.ForegroundProperty, "TextMutedBrush");
            button.BorderThickness = new Thickness(0, 0, 1, 1);
            button.Template = CreateSelectAllButtonTemplate();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ControlTemplate CreateSelectAllButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        var marker = new FrameworkElementFactory(typeof(Polygon));
        marker.SetValue(Polygon.PointsProperty, PointCollection.Parse("0,10 10,10 10,0"));
        marker.SetValue(Polygon.WidthProperty, 10.0);
        marker.SetValue(Polygon.HeightProperty, 10.0);
        marker.SetValue(Polygon.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        marker.SetValue(Polygon.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        marker.SetValue(Polygon.MarginProperty, new Thickness(0, 0, 4, 4));
        marker.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });

        border.AppendChild(marker);

        return new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };
    }

    private Style CreateSearchHighlightTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0)));

        if (FindResource("SearchHighlightBrushConverter") is IMultiValueConverter converter)
        {
            var backgroundBinding = new MultiBinding { Converter = converter };
            backgroundBinding.Bindings.Add(new Binding(nameof(TextBlock.Text))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.Self),
                Mode = BindingMode.OneWay
            });
            backgroundBinding.Bindings.Add(new Binding("DataContext.SearchText")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            backgroundBinding.Bindings.Add(new Binding("DataContext.IsSearchCaseSensitive")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            backgroundBinding.Bindings.Add(new Binding("DataContext.IsSearchWholeWord")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            backgroundBinding.Bindings.Add(new Binding("DataContext.IsSearchRegex")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });

            style.Setters.Add(new Setter(TextBlock.BackgroundProperty, backgroundBinding));
        }

        if (FindResource("SearchHighlightForegroundConverter") is IMultiValueConverter foregroundConverter)
        {
            var foregroundBinding = new MultiBinding { Converter = foregroundConverter };
            foregroundBinding.Bindings.Add(new Binding(nameof(TextBlock.Text))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.Self),
                Mode = BindingMode.OneWay
            });
            foregroundBinding.Bindings.Add(new Binding("DataContext.SearchText")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            foregroundBinding.Bindings.Add(new Binding("DataContext.IsSearchCaseSensitive")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            foregroundBinding.Bindings.Add(new Binding("DataContext.IsSearchWholeWord")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            foregroundBinding.Bindings.Add(new Binding("DataContext.IsSearchRegex")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
                Mode = BindingMode.OneWay
            });
            foregroundBinding.Bindings.Add(new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridCell), 1),
                Mode = BindingMode.OneWay
            });

            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, foregroundBinding));
        }

        return style;
    }

    private Style CreateCellPaintStyle(string columnName)
    {
        var style = new Style(typeof(DataGridCell));
        var paintBinding = CreateCellPaintBinding(columnName, CellPaintStateConverter.Instance);
        var backgroundBinding = CreateCellPaintBinding(columnName, _cellPaintBrushConverter);

        style.Setters.Add(new Setter(IsCellPaintedProperty, paintBinding));
        style.Setters.Add(new Setter(DataGridCell.BackgroundProperty, backgroundBinding));
        style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
        style.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));

        var paintedTrigger = new Trigger
        {
            Property = IsCellPaintedProperty,
            Value = true
        };
        paintedTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.Black));
        style.Triggers.Add(paintedTrigger);

        var selectedTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };
        selectedTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 99, 235))));
        selectedTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Brushes.White));
        style.Triggers.Add(selectedTrigger);

        return style;
    }

    private MultiBinding CreateCellPaintBinding(string columnName, IMultiValueConverter converter)
    {
        var binding = new MultiBinding
        {
            Converter = converter,
            ConverterParameter = columnName
        };

        AddCellPaintBindings(binding);
        return binding;
    }

    private void AddCellPaintBindings(MultiBinding binding)
    {
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding(nameof(CellPaintVersion))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
            Mode = BindingMode.OneWay
        });
        binding.Bindings.Add(new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
            Mode = BindingMode.OneWay
        });
        binding.Bindings.Add(new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CsvDocumentGrid), 1),
            Mode = BindingMode.OneWay
        });
    }

    private void SyncHorizontalScroll(DataGrid target, double offset, double change)
    {
        if (_syncingHorizontalScroll || change == 0)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(target);
        if (scrollViewer == null)
        {
            return;
        }

        _syncingHorizontalScroll = true;
        scrollViewer.ScrollToHorizontalOffset(offset);
        _syncingHorizontalScroll = false;
    }

    private void SyncColumnLayouts(DataGrid source, DataGrid target)
    {
        if (source.Columns.Count == 0 || target.Columns.Count == 0)
        {
            return;
        }

        try
        {
            _syncingColumnWidths = true;
            var count = Math.Min(source.Columns.Count, target.Columns.Count);
            for (var i = 0; i < count; i++)
            {
                if (target.Columns[i].DisplayIndex != source.Columns[i].DisplayIndex)
                {
                    target.Columns[i].DisplayIndex = source.Columns[i].DisplayIndex;
                }

                var actualWidth = source.Columns[i].ActualWidth;
                if (!double.IsNaN(actualWidth) && actualWidth > 0 && Math.Abs(target.Columns[i].ActualWidth - actualWidth) > 0.5)
                {
                    target.Columns[i].Width = new DataGridLength(actualWidth);
                }
            }
        }
        finally
        {
            _syncingColumnWidths = false;
        }
    }

    private static DataGridColumn? FindColumn(DataGrid dataGrid, string columnName)
    {
        return dataGrid.Columns.FirstOrDefault(column => GetColumnName(column) == columnName);
    }

    private static string? GetColumnName(DataGridColumn column)
    {
        if (!string.IsNullOrEmpty(column.SortMemberPath))
        {
            return column.SortMemberPath;
        }

        return column is DataGridBoundColumn { Binding: Binding binding } ? binding.Path?.Path : null;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T typedParent)
            {
                return typedParent;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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

    private readonly record struct PaintedCellKey(int RowIndex, string ColumnName);

    private sealed class CellPaintBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4
                || values[0] is not DataRowView rowView
                || values[2] is not DataGrid dataGrid
                || values[3] is not CsvDocumentGrid documentGrid
                || parameter is not string columnName)
            {
                return Brushes.Transparent;
            }

            return documentGrid.TryGetCellPaintBrush(dataGrid, rowView, columnName, out var brush)
                ? brush
                : Brushes.Transparent;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CellPaintStateConverter : IMultiValueConverter
    {
        public static CellPaintStateConverter Instance { get; } = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4
                || values[0] is not DataRowView rowView
                || values[2] is not DataGrid dataGrid
                || values[3] is not CsvDocumentGrid documentGrid
                || parameter is not string columnName)
            {
                return false;
            }

            return documentGrid.TryGetCellPaintBrush(dataGrid, rowView, columnName, out _);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
