using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace CsvViewer.Services;

public sealed class ClipboardService
{
    public void CopySelectedCells(DataGrid dataGrid)
    {
        if (dataGrid.SelectedCells.Count == 0)
        {
            return;
        }

        var rows = dataGrid.SelectedCells
            .GroupBy(cell => cell.Item)
            .ToList();

        var textRows = new List<string>();
        foreach (var row in rows)
        {
            var cells = row
                .OrderBy(cell => cell.Column.DisplayIndex)
                .Select(cell => cell.Column.GetCellContent(cell.Item))
                .OfType<TextBlock>()
                .Select(textBlock => textBlock.Text.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));

            textRows.Add(string.Join('\t', cells));
        }

        Clipboard.SetText(string.Join('\n', textRows));
    }
}
