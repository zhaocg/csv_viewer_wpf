using CsvViewer.Models;
using CsvViewer.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CsvViewer;

public partial class SvnLogWindow : Window, INotifyPropertyChanged
{
    private const int PageSize = 100;
    private readonly SvnFolderService _svnFolderService = new();
    private bool _isLoading;
    private bool _hasMore = true;
    private string _statusText = "准备加载日志...";
    private long? _nextStartRevision;

    public SvnLogWindow(string fileUrl)
    {
        InitializeComponent();
        FileUrl = fileUrl;
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileUrl { get; }
    public ObservableCollection<SvnLogEntry> Entries { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanLoadMore));
            }
        }
    }

    public bool CanLoadMore => !IsLoading && _hasMore;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadNextPageAsync();
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        await LoadNextPageAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async Task LoadNextPageAsync()
    {
        if (IsLoading || !_hasMore)
        {
            return;
        }

        IsLoading = true;
        StatusText = Entries.Count == 0 ? "正在加载最近 100 条日志..." : "正在加载下 100 条日志...";

        try
        {
            var entries = await _svnFolderService.GetLogAsync(FileUrl, PageSize, _nextStartRevision);
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            if (entries.Count < PageSize)
            {
                _hasMore = false;
            }

            if (Entries.Count > 0)
            {
                _nextStartRevision = Math.Max(Entries.Min(entry => entry.Revision) - 1, 1);
            }

            StatusText = Entries.Count == 0
                ? "没有找到 SVN 日志。"
                : $"已加载 {Entries.Count:N0} 条日志。";
        }
        catch (Exception ex)
        {
            StatusText = $"加载 SVN 日志失败: {ex.Message}";
            MessageBox.Show(ex.Message, "加载 SVN 日志失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanLoadMore));
        }
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
