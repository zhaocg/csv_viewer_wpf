using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CsvViewer.Commands;
using CsvViewer.Models;
using CsvViewer.Services;
using Microsoft.Win32;

namespace CsvViewer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly CsvFileService _csvFileService = new();
    private DataTable? _sourceTable;
    private DataView? _tableView;
    private string _statusText = "请打开或拖拽 CSV 文件。";
    private string _searchText = string.Empty;
    private string _fileName = "未打开文件";
    private string _fileSizeText = "-";
    private string _rowCountText = "0";
    private string _columnCountText = "0";
    private string _encodingName = "-";
    private string _delimiterName = "-";
    private ObservableCollection<FileTreeNode>? _folderTreeRoot;
    private FileTreeNode? _selectedTreeNode;
    private bool _isBusy;
    private string? _currentFilePath;
    private string _selectedEncoding = "Auto";
    private string _selectedDelimiter = "Auto";

    public MainViewModel()
    {
        OpenFileCommand = new RelayCommand(async _ => await OpenFileAsync(), _ => !IsBusy);
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolderAsync(), _ => !IsBusy);
        ReloadCommand = new RelayCommand(async _ => await ReloadAsync(), _ => !IsBusy && !string.IsNullOrEmpty(_currentFilePath));
        ApplySearchCommand = new RelayCommand(async _ => await ApplySearchAsync(), _ => !IsBusy && _sourceTable != null);
        ClearSearchCommand = new RelayCommand(_ => ClearSearch(), _ => !IsBusy && !string.IsNullOrEmpty(SearchText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ApplySearchCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public string[] EncodingOptions { get; } = ["Auto", "UTF-8", "GBK"];
    public string[] DelimiterOptions { get; } = ["Auto", "Comma (,)", "Semicolon (;)", "Tab", "Pipe (|)"];

    public DataView? TableView
    {
        get => _tableView;
        private set => SetProperty(ref _tableView, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string SelectedEncoding
    {
        get => _selectedEncoding;
        set => SetProperty(ref _selectedEncoding, value);
    }

    public string SelectedDelimiter
    {
        get => _selectedDelimiter;
        set => SetProperty(ref _selectedDelimiter, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        private set => SetProperty(ref _fileSizeText, value);
    }

    public string RowCountText
    {
        get => _rowCountText;
        private set => SetProperty(ref _rowCountText, value);
    }

    public string ColumnCountText
    {
        get => _columnCountText;
        private set => SetProperty(ref _columnCountText, value);
    }

    public string EncodingName
    {
        get => _encodingName;
        private set => SetProperty(ref _encodingName, value);
    }

    public string DelimiterName
    {
        get => _delimiterName;
        private set => SetProperty(ref _delimiterName, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ObservableCollection<FileTreeNode>? FolderTreeRoot
    {
        get => _folderTreeRoot;
        private set => SetProperty(ref _folderTreeRoot, value);
    }

    public FileTreeNode? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (SetProperty(ref _selectedTreeNode, value))
            {
                OnTreeNodeSelected(value);
            }
        }
    }

    public async Task LoadFileAsync(string filePath)
    {
        if (!IsSupportedFile(filePath))
        {
            MessageBox.Show("请选择 .csv 或 .txt 文件。", "不支持的文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusText = "正在加载文件...";

        try
        {
            var forcedEncoding = GetForcedEncoding();
            var forcedDelimiter = GetForcedDelimiter();
            var result = await Task.Run(() => _csvFileService.Load(filePath, forcedEncoding, forcedDelimiter));
            ApplyLoadedResult(result);
        }
        catch (Exception ex)
        {
            StatusText = "加载失败。";
            MessageBox.Show(ex.Message, "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            Title = "打开 CSV 文件"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFileAsync(dialog.FileName);
        }
    }

    private async Task ReloadAsync()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            await LoadFileAsync(_currentFilePath);
        }
    }

    private void ApplyLoadedResult(CsvLoadResult result)
    {
        _currentFilePath = result.FilePath;
        _sourceTable = result.Table;
        SearchText = string.Empty;
        TableView = result.Table.DefaultView;

        FileName = Path.GetFileName(result.FilePath);
        FileSizeText = FormatFileSize(result.FileSize);
        RowCountText = result.Table.Rows.Count.ToString("N0");
        ColumnCountText = result.Table.Columns.Count.ToString("N0");
        EncodingName = result.Encoding.WebName.ToUpperInvariant();
        DelimiterName = FormatDelimiter(result.Delimiter);
        StatusText = $"加载完成。{(result.HasHeader ? "已识别表头。" : "未识别表头，已生成默认列名。")}";
    }

    private async Task ApplySearchAsync()
    {
        if (_sourceTable == null)
        {
            return;
        }

        var keyword = SearchText.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            TableView = _sourceTable.DefaultView;
            StatusText = "已清除搜索。";
            RowCountText = _sourceTable.Rows.Count.ToString("N0");
            return;
        }

        IsBusy = true;
        StatusText = "正在搜索...";

        try
        {
            var filtered = await Task.Run(() => FilterTable(_sourceTable, keyword));
            TableView = filtered.DefaultView;
            RowCountText = filtered.Rows.Count.ToString("N0");
            StatusText = $"搜索完成，匹配 {filtered.Rows.Count:N0} 行。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
        if (_sourceTable != null)
        {
            TableView = _sourceTable.DefaultView;
            RowCountText = _sourceTable.Rows.Count.ToString("N0");
        }

        StatusText = "已清除搜索。";
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

    private Encoding? GetForcedEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return SelectedEncoding switch
        {
            "UTF-8" => new UTF8Encoding(false),
            "GBK" => Encoding.GetEncoding("GB18030"),
            _ => null
        };
    }

    private string? GetForcedDelimiter()
    {
        return SelectedDelimiter switch
        {
            "Comma (,)" => ",",
            "Semicolon (;)" => ";",
            "Tab" => "\t",
            "Pipe (|)" => "|",
            _ => null
        };
    }

    private static bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// 展开树节点，按需加载该目录下的子文件夹和 CSV 文件。
    /// </summary>
    public void ExpandTreeNode(FileTreeNode? node)
    {
        if (node is not { IsDirectory: true, HasDummyChild: true })
        {
            return;
        }

        try
        {
            node.Children.Clear();
            node.HasDummyChild = false;

            // 添加子文件夹
            foreach (var dirPath in Directory.GetDirectories(node.FullPath).OrderBy(Path.GetFileName))
            {
                var dirNode = new FileTreeNode
                {
                    Name = Path.GetFileName(dirPath),
                    FullPath = dirPath,
                    IsDirectory = true,
                    HasDummyChild = true
                };
                dirNode.Children.Add(new FileTreeNode()); // 占位节点，支持懒加载
                node.Children.Add(dirNode);
            }

            // 只添加 CSV 文件
            foreach (var filePath in Directory.GetFiles(node.FullPath, "*.csv").OrderBy(Path.GetFileName))
            {
                node.Children.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(filePath),
                    FullPath = filePath,
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 跳过无权限的目录
        }
    }

    /// <summary>
    /// 打开文件夹，构建文件夹树。
    /// </summary>
    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 CSV 文件的文件夹"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var rootPath = dialog.FolderName;
        IsBusy = true;
        StatusText = "正在加载文件夹树...";

        try
        {
            FolderTreeRoot = await Task.Run(() => BuildFolderTree(rootPath));
            StatusText = $"文件夹已加载: {rootPath}";
        }
        catch (Exception ex)
        {
            StatusText = "加载文件夹失败。";
            MessageBox.Show(ex.Message, "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 构建根节点下的文件夹树（懒加载，仅展开第一层）。
    /// </summary>
    private ObservableCollection<FileTreeNode> BuildFolderTree(string rootPath)
    {
        var rootNode = new FileTreeNode
        {
            Name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath = rootPath,
            IsDirectory = true,
            HasDummyChild = true
        };
        rootNode.Children.Add(new FileTreeNode()); // 占位节点

        return [rootNode];
    }

    /// <summary>
    /// 当用户在树中选中节点时触发：如果是 CSV 文件则直接加载。
    /// </summary>
    private async void OnTreeNodeSelected(FileTreeNode? node)
    {
        if (node is not { IsDirectory: false })
        {
            return;
        }

        if (string.Equals(node.FullPath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await LoadFileAsync(node.FullPath);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
