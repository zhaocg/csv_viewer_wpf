using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private const int MaxRecentFiles = 12;
    private const int MaxSearchResults = 100;

    private readonly AppStateService _appStateService = new();
    private readonly AppState _appState;
    private readonly CsvFileService _csvFileService = new();
    private List<FileTreeNode> _folderSearchIndex = [];
    private ObservableCollection<FileTreeNode>? _folderTreeRoot;
    private FileTreeNode? _selectedTreeNode;
    private CsvDocumentViewModel? _selectedDocument;
    private bool _isBusy;
    private bool _hideColumnHeaders;
    private string _statusText = "请打开或拖拽 CSV 文件。";
    private string _folderSearchText = string.Empty;
    private string _selectedEncoding = "Auto";
    private string _selectedDelimiter = "Auto";

    public MainViewModel()
    {
        _appState = _appStateService.Load();
        _appState.RecentFilePaths ??= [];
        _hideColumnHeaders = _appState.HideColumnHeaders;
        _selectedEncoding = NormalizeOption(_appState.SelectedEncoding, EncodingOptions);
        _selectedDelimiter = NormalizeOption(_appState.SelectedDelimiter, DelimiterOptions);
        LoadRecentFiles();

        OpenFileCommand = new RelayCommand(async _ => await OpenFileAsync(), _ => !IsBusy);
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolderAsync(), _ => !IsBusy);
        ReloadCommand = new RelayCommand(async _ => await ReloadAsync(), _ => !IsBusy && SelectedDocument != null);
        ApplySearchCommand = new RelayCommand(async _ => await ApplySearchAsync(), _ => !IsBusy && SelectedDocument != null);
        ClearSearchCommand = new RelayCommand(_ => ClearSearch(), _ => !IsBusy && !string.IsNullOrEmpty(SelectedDocument?.SearchText));
        CloseDocumentCommand = new RelayCommand(CloseDocument, _ => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ApplySearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand CloseDocumentCommand { get; }

    public string[] EncodingOptions { get; } = ["Auto", "UTF-8", "GBK"];
    public string[] DelimiterOptions { get; } = ["Auto", "Comma (,)", "Semicolon (;)", "Tab", "Pipe (|)"];
    public ObservableCollection<CsvDocumentViewModel> Documents { get; } = [];
    public ObservableCollection<FileTreeNode> FolderSearchResults { get; } = [];
    public ObservableCollection<FileTreeNode> RecentFiles { get; } = [];

    public CsvDocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (_selectedDocument != null)
            {
                _selectedDocument.PropertyChanged -= SelectedDocument_PropertyChanged;
            }

            if (SetProperty(ref _selectedDocument, value))
            {
                if (_selectedDocument != null)
                {
                    _selectedDocument.PropertyChanged += SelectedDocument_PropertyChanged;
                }

                RaiseSelectedDocumentProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => IsBusy ? _statusText : SelectedDocument?.StatusText ?? _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SelectedEncoding
    {
        get => _selectedEncoding;
        set
        {
            if (SetProperty(ref _selectedEncoding, value))
            {
                _appState.SelectedEncoding = value;
                SaveAppState();
            }
        }
    }

    public string SelectedDelimiter
    {
        get => _selectedDelimiter;
        set
        {
            if (SetProperty(ref _selectedDelimiter, value))
            {
                _appState.SelectedDelimiter = value;
                SaveAppState();
            }
        }
    }

    public string FolderSearchText
    {
        get => _folderSearchText;
        set
        {
            if (SetProperty(ref _folderSearchText, value))
            {
                OnPropertyChanged(nameof(HasFolderSearchText));
                UpdateFolderSearchResults();
            }
        }
    }

    public bool HasFolderSearchText => !string.IsNullOrWhiteSpace(FolderSearchText);

    public bool HideColumnHeaders
    {
        get => _hideColumnHeaders;
        set
        {
            if (SetProperty(ref _hideColumnHeaders, value))
            {
                _appState.HideColumnHeaders = value;
                SaveAppState();
            }
        }
    }

    public bool HasFrozenCells => (SelectedDocument?.FrozenRowCount ?? 0) > 0 || (SelectedDocument?.FrozenColumnCount ?? 0) > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(StatusText));
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

        var openedDocument = Documents.FirstOrDefault(document => string.Equals(document.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (openedDocument != null)
        {
            SelectedDocument = openedDocument;
            AddRecentFile(filePath);
            return;
        }

        IsBusy = true;
        StatusText = "正在加载文件...";

        try
        {
            var forcedEncoding = GetForcedEncoding();
            var forcedDelimiter = GetForcedDelimiter();
            var result = await Task.Run(() => _csvFileService.Load(filePath, forcedEncoding, forcedDelimiter));
            var document = new CsvDocumentViewModel(result, _csvFileService, forcedEncoding, forcedDelimiter);
            Documents.Add(document);
            SelectedDocument = document;
            SaveLastFolderPath(Path.GetDirectoryName(filePath));
            AddRecentFile(filePath);
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

    public async Task LoadFilesAsync(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            await LoadFileAsync(filePath);
        }
    }

    public async Task OpenFileNodeAsync(FileTreeNode? node)
    {
        if (node is not { IsDirectory: false })
        {
            return;
        }

        await LoadFileAsync(node.FullPath);
    }

    public void SetSelectedFrozenRowCount(int count)
    {
        SelectedDocument?.SetFrozenRowCount(count);
    }

    public void SetSelectedFrozenColumnCount(int count)
    {
        if (SelectedDocument != null)
        {
            SelectedDocument.FrozenColumnCount = count;
        }
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_appState.LastFolderPath) || !Directory.Exists(_appState.LastFolderPath))
        {
            return;
        }

        try
        {
            await LoadFolderContextAsync(_appState.LastFolderPath);
            StatusText = $"已恢复上次文件夹: {_appState.LastFolderPath}";
        }
        catch
        {
            StatusText = "请打开或拖拽 CSV 文件。";
        }
    }

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            Title = "打开 CSV 文件",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFilesAsync(dialog.FileNames);
        }
    }

    private async Task ReloadAsync()
    {
        if (SelectedDocument == null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在重新加载文件...";
        try
        {
            await SelectedDocument.ReloadAsync(GetForcedEncoding(), GetForcedDelimiter());
        }
        catch (Exception ex)
        {
            StatusText = "重新加载失败。";
            MessageBox.Show(ex.Message, "重新加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplySearchAsync()
    {
        if (SelectedDocument == null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在搜索...";
        try
        {
            await SelectedDocument.ApplySearchAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSearch()
    {
        SelectedDocument?.ClearSearch();
    }

    private void CloseDocument(object? parameter)
    {
        if (parameter is not CsvDocumentViewModel document)
        {
            return;
        }

        var index = Documents.IndexOf(document);
        Documents.Remove(document);

        if (SelectedDocument == document)
        {
            SelectedDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
        }

        if (Documents.Count == 0)
        {
            StatusText = "请打开或拖拽 CSV 文件。";
        }
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

    private static string NormalizeOption(string? value, string[] options)
    {
        return !string.IsNullOrWhiteSpace(value) && options.Contains(value) ? value : options[0];
    }

    private async Task LoadFolderContextAsync(string rootPath)
    {
        var folderContext = await Task.Run(() => new FolderContext(BuildFolderTree(rootPath), BuildSearchIndex(rootPath)));
        FolderTreeRoot = folderContext.Root;
        _folderSearchIndex = folderContext.SearchFiles;
        UpdateFolderSearchResults();
    }

    private void UpdateFolderSearchResults()
    {
        FolderSearchResults.Clear();
        var keyword = FolderSearchText.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        foreach (var node in _folderSearchIndex
            .Where(node => IsFileSearchMatch(node, keyword))
            .OrderBy(node => GetSearchRank(node, keyword))
            .ThenBy(node => node.Name)
            .ThenBy(node => node.FullPath)
            .Take(MaxSearchResults))
        {
            FolderSearchResults.Add(node);
        }
    }

    private static bool IsFileSearchMatch(FileTreeNode node, string keyword)
    {
        return IsFuzzyMatch(node.Name, keyword) || IsFuzzyMatch(node.FullPath, keyword);
    }

    private static int GetSearchRank(FileTreeNode node, string keyword)
    {
        if (node.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (node.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static bool IsFuzzyMatch(string text, string keyword)
    {
        if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var textIndex = 0;
        foreach (var keywordChar in keyword.Where(value => !char.IsWhiteSpace(value)))
        {
            var found = false;
            while (textIndex < text.Length)
            {
                if (char.ToUpperInvariant(text[textIndex++]) == char.ToUpperInvariant(keywordChar))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

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

            foreach (var dirPath in Directory.GetDirectories(node.FullPath).OrderBy(Path.GetFileName))
            {
                var dirNode = new FileTreeNode
                {
                    Name = Path.GetFileName(dirPath),
                    FullPath = dirPath,
                    IsDirectory = true,
                    HasDummyChild = true
                };
                dirNode.Children.Add(new FileTreeNode());
                node.Children.Add(dirNode);
            }

            foreach (var filePath in EnumerateSupportedFilesInDirectory(node.FullPath).OrderBy(Path.GetFileName))
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
        }
    }

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
            await LoadFolderContextAsync(rootPath);
            SaveLastFolderPath(rootPath);
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

    private ObservableCollection<FileTreeNode> BuildFolderTree(string rootPath)
    {
        var rootNode = new FileTreeNode
        {
            Name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath = rootPath,
            IsDirectory = true,
            HasDummyChild = true
        };
        rootNode.Children.Add(new FileTreeNode());

        return [rootNode];
    }

    private static List<FileTreeNode> BuildSearchIndex(string rootPath)
    {
        return EnumerateSupportedFilesRecursively(rootPath)
            .Select(filePath => new FileTreeNode
            {
                Name = Path.GetFileName(filePath),
                FullPath = filePath,
                IsDirectory = false
            })
            .OrderBy(node => node.Name)
            .ThenBy(node => node.FullPath)
            .ToList();
    }

    private static IEnumerable<string> EnumerateSupportedFilesInDirectory(string directoryPath)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var filePath in files.Where(IsSupportedFile))
        {
            yield return filePath;
        }
    }

    private static IEnumerable<string> EnumerateSupportedFilesRecursively(string rootPath)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            var directoryPath = pendingDirectories.Pop();
            foreach (var filePath in EnumerateSupportedFilesInDirectory(directoryPath))
            {
                yield return filePath;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directoryPath);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                pendingDirectories.Push(childDirectory);
            }
        }
    }

    private async void OnTreeNodeSelected(FileTreeNode? node)
    {
        await OpenFileNodeAsync(node);
    }

    private void SelectedDocument_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasFrozenCells));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RaiseSelectedDocumentProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasFrozenCells));
    }

    private void LoadRecentFiles()
    {
        RefreshRecentFiles();
        if (_appState.RecentFilePaths.RemoveAll(filePath => !File.Exists(filePath) || !IsSupportedFile(filePath)) > 0)
        {
            SaveAppState();
        }
    }

    private void AddRecentFile(string filePath)
    {
        if (!File.Exists(filePath) || !IsSupportedFile(filePath))
        {
            return;
        }

        _appState.RecentFilePaths.RemoveAll(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        _appState.RecentFilePaths.Insert(0, filePath);

        if (_appState.RecentFilePaths.Count > MaxRecentFiles)
        {
            _appState.RecentFilePaths.RemoveRange(MaxRecentFiles, _appState.RecentFilePaths.Count - MaxRecentFiles);
        }

        RefreshRecentFiles();
        SaveAppState();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var filePath in _appState.RecentFilePaths.Where(filePath => File.Exists(filePath) && IsSupportedFile(filePath)).Take(MaxRecentFiles))
        {
            RecentFiles.Add(new FileTreeNode
            {
                Name = Path.GetFileName(filePath),
                FullPath = filePath,
                IsDirectory = false
            });
        }
    }

    private void SaveLastFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        _appState.LastFolderPath = folderPath;
        SaveAppState();
    }

    private void SaveAppState()
    {
        try
        {
            _appStateService.Save(_appState);
        }
        catch
        {
        }
    }

    private sealed record FolderContext(ObservableCollection<FileTreeNode> Root, List<FileTreeNode> SearchFiles);

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
