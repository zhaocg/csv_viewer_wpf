using System.Collections.Generic;
using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CsvViewer.Services;

public sealed class ClipboardService
{
    public bool CopySelectedCells(DataGrid dataGrid)
    {
        if (dataGrid.SelectedCells.Count == 0)
        {
            return false;
        }

        var rows = dataGrid.SelectedCells
            .GroupBy(cell => cell.Item)
            .ToList();

        var textRows = new List<string>();
        foreach (var row in rows)
        {
            var cells = row
                .OrderBy(cell => cell.Column.DisplayIndex)
                .Select(GetCellText)
                .Select(SanitizeCellText);

            textRows.Add(string.Join('\t', cells));
        }

        if (textRows.Count == 0)
        {
            return false;
        }

        Clipboard.SetDataObject(string.Join('\n', textRows), true);
        return true;
    }

    private static string GetCellText(DataGridCellInfo cell)
    {
        if (cell.Item is DataRowView rowView
            && cell.Column is DataGridBoundColumn { Binding: Binding binding }
            && binding.Path?.Path is { Length: > 0 } columnName
            && rowView.Row.Table.Columns.Contains(columnName))
        {
            return Convert.ToString(rowView[columnName]) ?? string.Empty;
        }

        return cell.Column.GetCellContent(cell.Item) is TextBlock textBlock ? textBlock.Text : string.Empty;
    }

    private static string SanitizeCellText(string text)
    {
        return text.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
}
