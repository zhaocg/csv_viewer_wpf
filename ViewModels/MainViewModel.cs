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
    private const string DefaultSvnExcelPathTemplate = "svn://192.168.0.200/ccz_svn_project/branches/{0}/data/Excel";
    private static readonly string[] DefaultSvnBranches = ["dev", "release_1.1", "release_1.1_tc", "release_1.1_build_online"];

    private readonly AppStateService _appStateService = new();
    private readonly AppState _appState;
    private readonly CsvFileService _csvFileService = new();
    private readonly SvnFolderService _svnFolderService = new();
    private List<FileTreeNode> _folderSearchIndex = [];
    private ObservableCollection<FileTreeNode>? _folderTreeRoot;
    private FileTreeNode? _selectedTreeNode;
    private CsvDocumentViewModel? _selectedDocument;
    private bool _isBusy;
    private bool _hideColumnHeaders;
    private bool _isSvnMode;
    private string _statusText = "请打开或拖拽 CSV 文件。";
    private string _folderTreeMessage = string.Empty;
    private string _lastSvnUrl = string.Empty;
    private string _svnExcelPathTemplate = DefaultSvnExcelPathTemplate;
    private string _selectedSvnBranch = DefaultSvnBranches[0];
    private string _newSvnBranchName = string.Empty;
    private string? _selectedSvnBranchPreset;
    private string _selectedEncoding = "Auto";
    private string _selectedDelimiter = "Comma (,)";
    private string _selectedTheme = ThemeService.LightTheme;

    public MainViewModel()
    {
        _appState = _appStateService.Load();
        _appState.RecentFilePaths ??= [];
        _appState.RecentFiles ??= [];
        _appState.SvnBranches ??= [];
        if (_appState.SvnBranches.Count == 0)
        {
            _appState.SvnBranches.AddRange(DefaultSvnBranches);
        }

        _hideColumnHeaders = _appState.HideColumnHeaders;
        _isSvnMode = _appState.IsSvnMode;
        _svnExcelPathTemplate = string.IsNullOrWhiteSpace(_appState.SvnExcelPathTemplate) ? DefaultSvnExcelPathTemplate : _appState.SvnExcelPathTemplate;
        foreach (var branch in _appState.SvnBranches.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SvnBranches.Add(branch);
        }

        _selectedSvnBranch = NormalizeOption(_appState.SelectedSvnBranch, SvnBranches.ToArray());
        _selectedSvnBranchPreset = _selectedSvnBranch;
        _lastSvnUrl = GetCurrentSvnRootUrl();
        _selectedEncoding = NormalizeOption(_appState.SelectedEncoding, EncodingOptions);
        _selectedDelimiter = NormalizeOption(_appState.SelectedDelimiter, DelimiterOptions, "Comma (,)");
        _selectedTheme = NormalizeOption(_appState.SelectedTheme, ThemeOptions, ThemeService.LightTheme);
        ThemeService.Apply(_selectedTheme);
        LoadRecentFiles();

        OpenFileCommand = new RelayCommand(async _ => await OpenFileAsync(), _ => !IsBusy);
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolderAsync(), _ => !IsBusy);
        ReloadCommand = new RelayCommand(async _ => await ReloadAsync(), _ => !IsBusy && SelectedDocument != null);
        UpdateRemoteDocumentCommand = new RelayCommand(async _ => await UpdateRemoteDocumentAsync(), _ => !IsBusy && SelectedDocument?.IsRemote == true);
        ApplySearchCommand = new RelayCommand(async _ => await ApplySearchAsync(), _ => !IsBusy && SelectedDocument != null);
        ClearSearchCommand = new RelayCommand(_ => ClearSearch(), _ => !IsBusy && !string.IsNullOrEmpty(SelectedDocument?.SearchText));
        CloseDocumentCommand = new RelayCommand(CloseDocument, _ => !IsBusy);
        CloseOtherDocumentsCommand = new RelayCommand(CloseOtherDocuments, parameter => !IsBusy && parameter is CsvDocumentViewModel && Documents.Count > 1);
        CloseDocumentsToRightCommand = new RelayCommand(CloseDocumentsToRight, parameter => !IsBusy && parameter is CsvDocumentViewModel document && Documents.IndexOf(document) >= 0 && Documents.IndexOf(document) < Documents.Count - 1);
        CloseAllDocumentsCommand = new RelayCommand(_ => CloseAllDocuments(), _ => !IsBusy && Documents.Count > 0);
        AddSvnBranchCommand = new RelayCommand(_ => AddSvnBranch());
        RemoveSvnBranchCommand = new RelayCommand(_ => RemoveSelectedSvnBranch(), _ => SvnBranches.Count > 1 && !string.IsNullOrWhiteSpace(SelectedSvnBranchPreset));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand UpdateRemoteDocumentCommand { get; }
    public ICommand ApplySearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand CloseOtherDocumentsCommand { get; }
    public ICommand CloseDocumentsToRightCommand { get; }
    public ICommand CloseAllDocumentsCommand { get; }
    public ICommand AddSvnBranchCommand { get; }
    public ICommand RemoveSvnBranchCommand { get; }

    public string[] EncodingOptions { get; } = ["Auto", "UTF-8", "GBK"];
    public string[] DelimiterOptions { get; } = ["Auto", "Comma (,)", "Semicolon (;)", "Tab", "Pipe (|)"];
    public string[] ThemeOptions { get; } = [ThemeService.LightTheme, ThemeService.DarkTheme];
    public ObservableCollection<CsvDocumentViewModel> Documents { get; } = [];
    public ObservableCollection<FileTreeNode> RecentFiles { get; } = [];
    public ObservableCollection<string> SvnBranches { get; } = [];

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

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _appState.SelectedTheme = value;
                ThemeService.Apply(value);
                SaveAppState();
            }
        }
    }

    public string LastSvnUrl
    {
        get => _lastSvnUrl;
        private set => SetProperty(ref _lastSvnUrl, value);
    }

    public bool IsSvnMode
    {
        get => _isSvnMode;
        private set
        {
            if (SetProperty(ref _isSvnMode, value))
            {
                _appState.IsSvnMode = value;
                SaveAppState();
                OnPropertyChanged(nameof(IsLocalFolderMode));
            }
        }
    }

    public bool IsLocalFolderMode => !IsSvnMode;

    public string SvnExcelPathTemplate
    {
        get => _svnExcelPathTemplate;
        set
        {
            if (SetProperty(ref _svnExcelPathTemplate, value))
            {
                _appState.SvnExcelPathTemplate = value;
                LastSvnUrl = GetCurrentSvnRootUrl();
                RefreshRecentFiles();
                SaveAppState();
            }
        }
    }

    public string SelectedSvnBranch
    {
        get => _selectedSvnBranch;
        set
        {
            if (SetProperty(ref _selectedSvnBranch, value))
            {
                _appState.SelectedSvnBranch = value;
                SelectedSvnBranchPreset = value;
                LastSvnUrl = GetCurrentSvnRootUrl();
                RefreshRecentFiles();
                SaveAppState();
            }
        }
    }

    public string NewSvnBranchName
    {
        get => _newSvnBranchName;
        set => SetProperty(ref _newSvnBranchName, value);
    }

    public string? SelectedSvnBranchPreset
    {
        get => _selectedSvnBranchPreset;
        set
        {
            if (SetProperty(ref _selectedSvnBranchPreset, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

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
    public bool IsRemoteDocument => SelectedDocument?.IsRemote == true;
    public string FreezeStatusText
    {
        get
        {
            var rowCount = SelectedDocument?.FrozenRowCount ?? 0;
            var columnCount = SelectedDocument?.FrozenColumnCount ?? 0;
            if (rowCount == 0 && columnCount == 0)
            {
                return "冻结: 无";
            }

            var parts = new List<string>();
            if (rowCount > 0)
            {
                parts.Add($"前 {rowCount} 行");
            }

            if (columnCount > 0)
            {
                parts.Add($"前 {columnCount} 列");
            }

            return $"冻结: {string.Join("，", parts)}";
        }
    }

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

    public string FolderTreeMessage
    {
        get => _folderTreeMessage;
        private set
        {
            if (SetProperty(ref _folderTreeMessage, value))
            {
                OnPropertyChanged(nameof(HasFolderTreeMessage));
            }
        }
    }

    public bool HasFolderTreeMessage => !string.IsNullOrWhiteSpace(FolderTreeMessage);

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

    private async Task LoadSvnFileAsync(FileTreeNode node)
    {
        var relativePath = node.RelativePath;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var openedDocument = Documents.FirstOrDefault(document => document.IsRemote
            && string.Equals(document.RemoteRelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (openedDocument != null)
        {
            SelectedDocument = openedDocument;
            AddRecentSvnFile(relativePath);
            return;
        }

        IsBusy = true;
        StatusText = "正在读取 SVN 文件...";

        try
        {
            var forcedEncoding = GetForcedEncoding();
            var forcedDelimiter = GetForcedDelimiter();
            var svnFileUrl = GetSvnFileUrl(relativePath);
            var fileName = Path.GetFileName(relativePath);
            var result = await LoadSvnCsvResultAsync(svnFileUrl, fileName, forcedEncoding, forcedDelimiter);
            var document = new CsvDocumentViewModel(
                result,
                _csvFileService,
                forcedEncoding,
                forcedDelimiter,
                sourcePath: svnFileUrl,
                isRemote: true,
                remoteRelativePath: relativePath,
                reloadAsync: (encoding, delimiter) => LoadSvnCsvResultAsync(GetSvnFileUrl(relativePath), fileName, encoding, delimiter));
            Documents.Add(document);
            SelectedDocument = document;
            AddRecentSvnFile(relativePath);
        }
        catch (Exception ex)
        {
            StatusText = "读取 SVN 文件失败。";
            MessageBox.Show(ex.Message, "读取 SVN 文件失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<CsvLoadResult> LoadSvnCsvResultAsync(string svnFileUrl, string fileName, Encoding? forcedEncoding, string? forcedDelimiter)
    {
        var localPath = await _svnFolderService.CacheFileAsync(svnFileUrl, fileName);
        return await Task.Run(() => _csvFileService.Load(localPath, forcedEncoding, forcedDelimiter));
    }

    public async Task OpenFileNodeAsync(FileTreeNode? node)
    {
        if (node is not { IsDirectory: false })
        {
            return;
        }

        if (node.IsRemote)
        {
            await LoadSvnFileAsync(node);
            return;
        }

        await LoadFileAsync(node.FullPath);
    }

    public IReadOnlyList<FileTreeNode> GetQuickOpenResults(string keyword, int maxResults = MaxSearchResults)
    {
        var trimmedKeyword = keyword.Trim();
        if (string.IsNullOrEmpty(trimmedKeyword))
        {
            return RecentFiles.Take(maxResults).ToList();
        }

        return _folderSearchIndex
            .Where(node => IsFileSearchMatch(node, trimmedKeyword))
            .OrderBy(node => GetSearchRank(node, trimmedKeyword))
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public async Task SetSvnModeAsync(bool enabled)
    {
        IsSvnMode = enabled;
        if (enabled)
        {
            await LoadCurrentSvnFolderAsync(reloadOpenedDocuments: false);
            return;
        }

        await LoadLastLocalFolderAsync();
    }

    public async Task ChangeSvnBranchAsync(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || string.Equals(SelectedSvnBranch, branch, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedSvnBranch = branch;
        if (IsSvnMode)
        {
            await LoadCurrentSvnFolderAsync(reloadOpenedDocuments: true);
        }
    }

    public async Task RefreshSvnModeAsync()
    {
        if (IsSvnMode)
        {
            await LoadCurrentSvnFolderAsync(reloadOpenedDocuments: false);
        }
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

    public async Task InitializeAsync(IProgress<StartupProgress>? progress = null)
    {
        progress?.Report(new StartupProgress("正在读取启动配置...", 50));

        if (IsSvnMode)
        {
            progress?.Report(new StartupProgress($"正在加载 SVN 分支: {SelectedSvnBranch}...", 60));
            await LoadCurrentSvnFolderAsync(reloadOpenedDocuments: false);
            progress?.Report(new StartupProgress("SVN 分支加载完成", 85));
            return;
        }

        progress?.Report(new StartupProgress("正在恢复上次本地文件夹...", 60));
        await LoadLastLocalFolderAsync();
        progress?.Report(new StartupProgress("本地工作区恢复完成", 85));
    }

    private async Task LoadLastLocalFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(_appState.LastFolderPath) || !Directory.Exists(_appState.LastFolderPath))
        {
            ClearFolderTree(string.Empty);
            StatusText = "请打开或拖拽 CSV 文件。";
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

    private async Task LoadCurrentSvnFolderAsync(bool reloadOpenedDocuments)
    {
        IsBusy = true;
        LastSvnUrl = GetCurrentSvnRootUrl();
        StatusText = $"正在加载 SVN 分支: {SelectedSvnBranch}...";

        try
        {
            var relativeFilePaths = await _svnFolderService.ListFilesAsync(LastSvnUrl);
            var svnFolderContext = BuildSvnFolderContext(LastSvnUrl, relativeFilePaths);
            FolderTreeRoot = svnFolderContext.Root;
            FolderTreeMessage = string.Empty;
            _folderSearchIndex = svnFolderContext.SearchFiles;

            if (reloadOpenedDocuments)
            {
                await ReloadRemoteDocumentsForCurrentBranchAsync();
            }

            StatusText = $"SVN 分支已加载: {SelectedSvnBranch}";
        }
        catch (Exception ex)
        {
            var message = $"SVN 分支 {SelectedSvnBranch} 加载失败。{Environment.NewLine}{ex.Message}";
            ClearFolderTree(message);
            MarkRemoteDocumentsUnavailable(message);
            StatusText = "加载 SVN 分支失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadRemoteDocumentsForCurrentBranchAsync()
    {
        var failedDocuments = new List<string>();
        foreach (var document in Documents.Where(document => document.IsRemote && !string.IsNullOrWhiteSpace(document.RemoteRelativePath)).ToList())
        {
            var relativePath = document.RemoteRelativePath!;
            try
            {
                await document.ReloadAsync(GetForcedEncoding(), GetForcedDelimiter());
                ConfigureRemoteDocumentSource(document, relativePath);
            }
            catch
            {
                document.MarkUnavailable($"当前分支 {SelectedSvnBranch} 中不存在或无法读取该表。{Environment.NewLine}已清空旧内容，避免误读。{Environment.NewLine}{document.RemoteRelativePath}");
                failedDocuments.Add(document.FileName);
            }
        }

        if (failedDocuments.Count > 0)
        {
            StatusText = $"当前分支中有 {failedDocuments.Count} 个 SVN 表格不可用，已清空旧内容。";
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

    private async Task UpdateRemoteDocumentAsync()
    {
        if (SelectedDocument?.IsRemote != true)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在更新 SVN 表格...";
        try
        {
            await SelectedDocument.ReloadAsync(GetForcedEncoding(), GetForcedDelimiter());
            if (!string.IsNullOrWhiteSpace(SelectedDocument.RemoteRelativePath))
            {
                ConfigureRemoteDocumentSource(SelectedDocument, SelectedDocument.RemoteRelativePath);
            }
        }
        catch (Exception ex)
        {
            StatusText = "更新 SVN 表格失败。";
            MessageBox.Show(ex.Message, "更新 SVN 表格失败", MessageBoxButton.OK, MessageBoxImage.Error);
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

        CommandManager.InvalidateRequerySuggested();
    }

    private void CloseOtherDocuments(object? parameter)
    {
        if (parameter is not CsvDocumentViewModel document || !Documents.Contains(document))
        {
            return;
        }

        for (var index = Documents.Count - 1; index >= 0; index--)
        {
            if (Documents[index] != document)
            {
                Documents.RemoveAt(index);
            }
        }

        SelectedDocument = document;
        CommandManager.InvalidateRequerySuggested();
    }

    private void CloseDocumentsToRight(object? parameter)
    {
        if (parameter is not CsvDocumentViewModel document)
        {
            return;
        }

        var documentIndex = Documents.IndexOf(document);
        if (documentIndex < 0)
        {
            return;
        }

        for (var index = Documents.Count - 1; index > documentIndex; index--)
        {
            Documents.RemoveAt(index);
        }

        SelectedDocument = document;
        CommandManager.InvalidateRequerySuggested();
    }

    private void CloseAllDocuments()
    {
        Documents.Clear();
        SelectedDocument = null;
        StatusText = "请打开或拖拽 CSV 文件。";
        CommandManager.InvalidateRequerySuggested();
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

    private static string NormalizeOption(string? value, string[] options, string? defaultValue = null)
    {
        return !string.IsNullOrWhiteSpace(value) && options.Contains(value) ? value : defaultValue ?? options[0];
    }

    private string GetCurrentSvnRootUrl()
    {
        var template = string.IsNullOrWhiteSpace(SvnExcelPathTemplate) ? DefaultSvnExcelPathTemplate : SvnExcelPathTemplate;
        return template.Contains("{0}", StringComparison.Ordinal) ? template.Replace("{0}", SelectedSvnBranch) : template;
    }

    public async Task<IReadOnlyList<string>> DiscoverSvnBranchesAsync()
    {
        var template = string.IsNullOrWhiteSpace(SvnExcelPathTemplate) ? DefaultSvnExcelPathTemplate : SvnExcelPathTemplate;
        var placeholderIndex = template.IndexOf("{0}", StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            throw new InvalidOperationException("SVN Excel 路径模板必须包含 {0}，才能自动发现分支。");
        }

        var branchesRootUrl = template[..placeholderIndex].TrimEnd('/');
        if (string.IsNullOrWhiteSpace(branchesRootUrl))
        {
            throw new InvalidOperationException("无法从 SVN Excel 路径模板解析分支目录。");
        }

        var branchNames = await _svnFolderService.ListDirectoriesAsync(branchesRootUrl);
        return branchNames
            .Where(branch => !SvnBranches.Any(existing => string.Equals(existing, branch, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string GetSvnFileUrl(string relativePath)
    {
        return SvnFolderService.CombineUrl(GetCurrentSvnRootUrl(), relativePath);
    }

    private void ConfigureRemoteDocumentSource(CsvDocumentViewModel document, string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        document.SetRemoteSource(
            GetSvnFileUrl(relativePath),
            (encoding, delimiter) => LoadSvnCsvResultAsync(GetSvnFileUrl(relativePath), fileName, encoding, delimiter));
    }

    private void AddSvnBranch()
    {
        var branchName = NewSvnBranchName.Trim();
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        if (!SvnBranches.Any(branch => string.Equals(branch, branchName, StringComparison.OrdinalIgnoreCase)))
        {
            SvnBranches.Add(branchName);
            SaveSvnBranches();
            CommandManager.InvalidateRequerySuggested();
        }

        SelectedSvnBranchPreset = branchName;
        NewSvnBranchName = string.Empty;
    }

    public void AddSvnBranches(IEnumerable<string> branchNames)
    {
        var addedBranches = branchNames
            .Select(branch => branch.Trim())
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Where(branch => !SvnBranches.Any(existing => string.Equals(existing, branch, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (addedBranches.Length == 0)
        {
            return;
        }

        foreach (var branch in addedBranches)
        {
            SvnBranches.Add(branch);
        }

        SelectedSvnBranchPreset = addedBranches[0];
        SaveSvnBranches();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveSelectedSvnBranch()
    {
        var branchName = SelectedSvnBranchPreset;
        if (string.IsNullOrWhiteSpace(branchName) || SvnBranches.Count <= 1)
        {
            return;
        }

        var branch = SvnBranches.FirstOrDefault(value => string.Equals(value, branchName, StringComparison.OrdinalIgnoreCase));
        if (branch == null)
        {
            return;
        }

        SvnBranches.Remove(branch);
        if (string.Equals(SelectedSvnBranch, branch, StringComparison.OrdinalIgnoreCase))
        {
            SelectedSvnBranch = SvnBranches[0];
        }

        SelectedSvnBranchPreset = SvnBranches.FirstOrDefault();
        SaveSvnBranches();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SaveSvnBranches()
    {
        _appState.SvnBranches = SvnBranches.ToList();
        SaveAppState();
    }

    private async Task LoadFolderContextAsync(string rootPath)
    {
        var folderContext = await Task.Run(() => new FolderContext(BuildFolderTree(rootPath), BuildSearchIndex(rootPath)));
        FolderTreeRoot = folderContext.Root;
        FolderTreeMessage = string.Empty;
        _folderSearchIndex = folderContext.SearchFiles;
    }

    private void ClearFolderTree(string message)
    {
        FolderTreeRoot = null;
        FolderTreeMessage = message;
        _folderSearchIndex = [];
    }

    private void MarkRemoteDocumentsUnavailable(string message)
    {
        foreach (var document in Documents.Where(document => document.IsRemote).ToList())
        {
            document.MarkUnavailable(message);
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
        IsSvnMode = false;
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

    private static FolderContext BuildSvnFolderContext(string rootUrl, IEnumerable<string> relativeFilePaths)
    {
        var rootNode = new FileTreeNode
        {
            Name = GetSvnRootName(rootUrl),
            FullPath = rootUrl,
            RelativePath = string.Empty,
            IsDirectory = true,
            IsRemote = true
        };
        var directories = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = rootNode
        };
        var searchFiles = new List<FileTreeNode>();

        foreach (var relativeFilePath in relativeFilePaths
            .Select(path => path.Replace('\\', '/').Trim('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path) && IsSupportedFile(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = relativeFilePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var parentNode = rootNode;
            var currentDirectoryPath = string.Empty;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                currentDirectoryPath = string.IsNullOrEmpty(currentDirectoryPath) ? parts[i] : $"{currentDirectoryPath}/{parts[i]}";
                if (!directories.TryGetValue(currentDirectoryPath, out var directoryNode))
                {
                    directoryNode = new FileTreeNode
                    {
                        Name = parts[i],
                        FullPath = SvnFolderService.CombineUrl(rootUrl, currentDirectoryPath),
                        RelativePath = currentDirectoryPath,
                        IsDirectory = true,
                        IsRemote = true
                    };
                    parentNode.Children.Add(directoryNode);
                    directories[currentDirectoryPath] = directoryNode;
                }

                parentNode = directoryNode;
            }

            var fileNode = new FileTreeNode
            {
                Name = parts[^1],
                FullPath = SvnFolderService.CombineUrl(rootUrl, relativeFilePath),
                RelativePath = relativeFilePath,
                IsDirectory = false,
                IsRemote = true
            };
            parentNode.Children.Add(fileNode);
            searchFiles.Add(fileNode);
        }

        SortTreeChildren(rootNode);

        return new FolderContext([rootNode], searchFiles);
    }

    private static void SortTreeChildren(FileTreeNode node)
    {
        var orderedChildren = node.Children
            .OrderByDescending(child => child.IsDirectory)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(child => child.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        node.Children.Clear();
        foreach (var child in orderedChildren)
        {
            node.Children.Add(child);
            if (child.IsDirectory)
            {
                SortTreeChildren(child);
            }
        }
    }

    private static string GetSvnRootName(string rootUrl)
    {
        var trimmed = rootUrl.TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            return string.IsNullOrWhiteSpace(name) ? uri.Host : name;
        }

        return Path.GetFileName(trimmed);
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
        OnPropertyChanged(nameof(IsRemoteDocument));
        OnPropertyChanged(nameof(FreezeStatusText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RaiseSelectedDocumentProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasFrozenCells));
        OnPropertyChanged(nameof(IsRemoteDocument));
        OnPropertyChanged(nameof(FreezeStatusText));
    }

    private void LoadRecentFiles()
    {
        var changed = false;
        foreach (var filePath in _appState.RecentFilePaths)
        {
            if (_appState.RecentFiles.Any(entry => !entry.IsRemote && string.Equals(entry.Path, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (File.Exists(filePath) && IsSupportedFile(filePath))
            {
                _appState.RecentFiles.Add(new RecentFileEntry { IsRemote = false, Path = filePath });
                changed = true;
            }
        }

        _appState.RecentFilePaths.Clear();
        if (PruneRecentEntries())
        {
            changed = true;
        }

        RefreshRecentFiles();
        if (changed)
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

        AddRecentEntry(new RecentFileEntry { IsRemote = false, Path = filePath });
    }

    private void AddRecentSvnFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || !IsSupportedFile(relativePath))
        {
            return;
        }

        AddRecentEntry(new RecentFileEntry { IsRemote = true, Path = relativePath.Replace('\\', '/').Trim('/') });
    }

    private void AddRecentEntry(RecentFileEntry entry)
    {
        _appState.RecentFiles.RemoveAll(item => item.IsRemote == entry.IsRemote && string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        _appState.RecentFiles.Insert(0, entry);
        PruneRecentEntries();
        RefreshRecentFiles();
        SaveAppState();
    }

    private bool PruneRecentEntries()
    {
        var originalCount = _appState.RecentFiles.Count;
        _appState.RecentFiles.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.Path)
            || (!entry.IsRemote && (!File.Exists(entry.Path) || !IsSupportedFile(entry.Path)))
            || (entry.IsRemote && !IsSupportedFile(entry.Path)));

        if (_appState.RecentFiles.Count > MaxRecentFiles)
        {
            _appState.RecentFiles.RemoveRange(MaxRecentFiles, _appState.RecentFiles.Count - MaxRecentFiles);
        }

        return _appState.RecentFiles.Count != originalCount;
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var entry in _appState.RecentFiles.Take(MaxRecentFiles))
        {
            if (entry.IsRemote)
            {
                var relativePath = entry.Path.Replace('\\', '/').Trim('/');
                RecentFiles.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(relativePath),
                    FullPath = GetSvnFileUrl(relativePath),
                    RelativePath = relativePath,
                    IsDirectory = false,
                    IsRemote = true
                });
                continue;
            }

            if (File.Exists(entry.Path) && IsSupportedFile(entry.Path))
            {
                RecentFiles.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(entry.Path),
                    FullPath = entry.Path,
                    IsDirectory = false
                });
            }
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
