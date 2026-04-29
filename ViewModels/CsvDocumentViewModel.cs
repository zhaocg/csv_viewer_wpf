using System;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CsvViewer.Models;
using CsvViewer.Services;

namespace CsvViewer.ViewModels;

public sealed class CsvDocumentViewModel : INotifyPropertyChanged
{
    private readonly CsvFileService _csvFileService;
    private Func<Encoding?, string?, Task<CsvLoadResult>>? _reloadAsync;
    private Encoding? _forcedEncoding;
    private string? _forcedDelimiter;
    private DataTable _sourceTable;
    private DataTable _activeTable;
    private DataView? _tableView;
    private DataView? _frozenTableView;
    private DataView? _scrollableTableView;
    private string _statusText;
    private string _displayMessage = string.Empty;
    private string _searchText = string.Empty;
    private string _rowCountText;
    private bool _hasFrozenRows;
    private int _frozenRowCount;
    private int _frozenColumnCount;

    public CsvDocumentViewModel(
        CsvLoadResult result,
        CsvFileService csvFileService,
        Encoding? forcedEncoding,
        string? forcedDelimiter,
        string? sourcePath = null,
        bool isRemote = false,
        string? remoteRelativePath = null,
        Func<Encoding?, string?, Task<CsvLoadResult>>? reloadAsync = null)
    {
        _csvFileService = csvFileService;
        _reloadAsync = reloadAsync;
        _forcedEncoding = forcedEncoding;
        _forcedDelimiter = forcedDelimiter;
        FilePath = sourcePath ?? result.FilePath;
        IsRemote = isRemote;
        RemoteRelativePath = remoteRelativePath;
        FileName = GetFileName(FilePath);
        FileSizeText = FormatFileSize(result.FileSize);
        ColumnCountText = result.Table.Columns.Count.ToString("N0");
        EncodingName = result.Encoding.WebName.ToUpperInvariant();
        DelimiterName = FormatDelimiter(result.Delimiter);
        _sourceTable = result.Table;
        _activeTable = result.Table;
        _rowCountText = result.Table.Rows.Count.ToString("N0");
        _statusText = $"加载完成。{(result.HasHeader ? "已识别表头。" : "未识别表头，已生成 Excel 风格列名。")}";
        SetActiveTable(result.Table);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; private set; }
    public bool IsRemote { get; }
    public string? RemoteRelativePath { get; }
    public string FileName { get; private set; }
    public string FileSizeText { get; private set; }
    public string ColumnCountText { get; private set; }
    public string EncodingName { get; private set; }
    public string DelimiterName { get; private set; }

    public DataView? TableView
    {
        get => _tableView;
        private set => SetProperty(ref _tableView, value);
    }

    public DataView? FrozenTableView
    {
        get => _frozenTableView;
        private set => SetProperty(ref _frozenTableView, value);
    }

    public DataView? ScrollableTableView
    {
        get => _scrollableTableView;
        private set => SetProperty(ref _scrollableTableView, value);
    }

    public bool HasFrozenRows
    {
        get => _hasFrozenRows;
        private set => SetProperty(ref _hasFrozenRows, value);
    }

    public int FrozenRowCount
    {
        get => _frozenRowCount;
        private set => SetProperty(ref _frozenRowCount, value);
    }

    public int FrozenColumnCount
    {
        get => _frozenColumnCount;
        set => SetProperty(ref _frozenColumnCount, Math.Clamp(value, 0, _activeTable.Columns.Count));
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DisplayMessage
    {
        get => _displayMessage;
        private set
        {
            if (SetProperty(ref _displayMessage, value))
            {
                OnPropertyChanged(nameof(HasDisplayMessage));
            }
        }
    }

    public bool HasDisplayMessage => !string.IsNullOrWhiteSpace(DisplayMessage);

    public string RowCountText
    {
        get => _rowCountText;
        private set => SetProperty(ref _rowCountText, value);
    }

    public async Task ReloadAsync(Encoding? forcedEncoding, string? forcedDelimiter)
    {
        _forcedEncoding = forcedEncoding;
        _forcedDelimiter = forcedDelimiter;
        StatusText = "正在重新加载文件...";
        var result = _reloadAsync != null
            ? await _reloadAsync(_forcedEncoding, _forcedDelimiter)
            : await Task.Run(() => _csvFileService.Load(FilePath, _forcedEncoding, _forcedDelimiter));
        ApplyReloadedResult(result);
    }

    public void SetRemoteSource(string sourcePath, Func<Encoding?, string?, Task<CsvLoadResult>> reloadAsync)
    {
        FilePath = sourcePath;
        _reloadAsync = reloadAsync;
        FileName = GetFileName(FilePath);
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
    }

    public void MarkReloadFailed(string message)
    {
        StatusText = message;
    }

    public void MarkUnavailable(string message)
    {
        _sourceTable = new DataTable();
        _activeTable = _sourceTable;
        TableView = _activeTable.DefaultView;
        FrozenTableView = null;
        ScrollableTableView = _activeTable.DefaultView;
        FrozenRowCount = 0;
        FrozenColumnCount = 0;
        HasFrozenRows = false;
        RowCountText = "0";
        ColumnCountText = "0";
        FileSizeText = "-";
        EncodingName = "-";
        DelimiterName = "-";
        DisplayMessage = message;
        StatusText = message;
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(ColumnCountText));
        OnPropertyChanged(nameof(EncodingName));
        OnPropertyChanged(nameof(DelimiterName));
    }

    public async Task ApplySearchAsync()
    {
        var keyword = SearchText.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            SetActiveTable(_sourceTable);
            StatusText = "已清除搜索。";
            RowCountText = _sourceTable.Rows.Count.ToString("N0");
            return;
        }

        StatusText = "正在搜索...";
        var filtered = await Task.Run(() => FilterTable(_sourceTable, keyword));
        SetActiveTable(filtered);
        RowCountText = filtered.Rows.Count.ToString("N0");
        StatusText = $"搜索完成，匹配 {filtered.Rows.Count:N0} 行。";
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        SetActiveTable(_sourceTable);
        RowCountText = _sourceTable.Rows.Count.ToString("N0");
        StatusText = "已清除搜索。";
    }

    public void SetFrozenRowCount(int count)
    {
        FrozenRowCount = Math.Clamp(count, 0, _sourceTable.Rows.Count);
        RebuildSplitViews();
    }

    private void ApplyReloadedResult(CsvLoadResult result)
    {
        _sourceTable = result.Table;
        FileName = GetFileName(FilePath);
        FileSizeText = FormatFileSize(result.FileSize);
        ColumnCountText = result.Table.Columns.Count.ToString("N0");
        EncodingName = result.Encoding.WebName.ToUpperInvariant();
        DelimiterName = FormatDelimiter(result.Delimiter);
        DisplayMessage = string.Empty;
        SearchText = string.Empty;
        RowCountText = result.Table.Rows.Count.ToString("N0");
        FrozenColumnCount = Math.Clamp(FrozenColumnCount, 0, result.Table.Columns.Count);
        SetActiveTable(result.Table);
        StatusText = $"重新加载完成。{(result.HasHeader ? "已识别表头。" : "未识别表头，已生成 Excel 风格列名。")}";
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(ColumnCountText));
        OnPropertyChanged(nameof(EncodingName));
        OnPropertyChanged(nameof(DelimiterName));
    }

    private void SetActiveTable(DataTable table)
    {
        _activeTable = table;
        TableView = table.DefaultView;
        RebuildSplitViews();
    }

    private void RebuildSplitViews()
    {
        FrozenRowCount = Math.Clamp(FrozenRowCount, 0, _sourceTable.Rows.Count);
        HasFrozenRows = FrozenRowCount > 0;

        if (FrozenRowCount == 0)
        {
            FrozenTableView = null;
            ScrollableTableView = _activeTable.DefaultView;
            return;
        }

        FrozenTableView = CopyRows(_sourceTable, 0, FrozenRowCount).DefaultView;
        ScrollableTableView = ReferenceEquals(_activeTable, _sourceTable)
            ? CopyRows(_activeTable, FrozenRowCount, _activeTable.Rows.Count - FrozenRowCount).DefaultView
            : _activeTable.DefaultView;
    }

    private static DataTable FilterTable(DataTable table, string keyword)
    {
        var filtered = table.Clone();
        foreach (DataRow row in table.Rows)
        {
            if (row.ItemArray.Any(value => Convert.ToString(value)?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                filtered.ImportRow(row);
            }
        }

        return filtered;
    }

    private static DataTable CopyRows(DataTable table, int startIndex, int count)
    {
        var copy = table.Clone();
        var endIndex = Math.Min(startIndex + count, table.Rows.Count);
        for (var i = startIndex; i < endIndex; i++)
        {
            copy.ImportRow(table.Rows[i]);
        }

        return copy;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatDelimiter(string delimiter)
    {
        return delimiter switch
        {
            "\t" => "Tab",
            "," => "Comma",
            ";" => "Semicolon",
            "|" => "Pipe",
            _ => delimiter
        };
    }

    private static string GetFileName(string pathOrUrl)
    {
        var normalizedPath = pathOrUrl.TrimEnd('/', '\\');
        var fileNameStartIndex = normalizedPath.LastIndexOfAny(['/', '\\']) + 1;
        var fileName = fileNameStartIndex > 0 ? normalizedPath[fileNameStartIndex..] : Path.GetFileName(normalizedPath);
        return Uri.UnescapeDataString(fileName);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
