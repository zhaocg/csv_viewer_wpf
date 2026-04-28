using CsvViewer.Models;
using CsvViewer.Services;
using CsvViewer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CsvViewer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ClipboardService _clipboardService = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void CsvDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex() + 1;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await viewModel.LoadFileAsync(files[0]);
        }
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        _clipboardService.CopySelectedCells(CsvDataGrid);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("CSV Viewer\nWindows CSV 只读阅读器", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FolderTreeView_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is FileTreeNode node)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ExpandTreeNode(node);
            }
        }
    }

    private async void FolderTreeView_ItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is FileTreeNode node && !node.IsDirectory)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.LoadFileAsync(node.FullPath);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
