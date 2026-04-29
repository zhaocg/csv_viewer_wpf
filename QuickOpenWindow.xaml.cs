using CsvViewer.Models;
using CsvViewer.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CsvViewer;

public partial class QuickOpenWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ObservableCollection<FileTreeNode> _results = [];

    public QuickOpenWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        ResultsListBox.ItemsSource = _results;
        Loaded += QuickOpenWindow_Loaded;
    }

    private void QuickOpenWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshResults();
        SearchTextBox.Focus();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private async void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await OpenSelectedAsync();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                await OpenSelectedAsync();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void RefreshResults()
    {
        _results.Clear();
        foreach (var result in _viewModel.GetQuickOpenResults(SearchTextBox.Text))
        {
            _results.Add(result);
        }

        ResultsListBox.SelectedIndex = _results.Count > 0 ? 0 : -1;
        HintTextBlock.Text = _results.Count == 0
            ? "没有匹配结果。"
            : "输入文件名进行模糊搜索，Enter 打开，Esc 关闭。空输入显示最近打开。";
    }

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var nextIndex = ResultsListBox.SelectedIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = _results.Count - 1;
        }
        else if (nextIndex >= _results.Count)
        {
            nextIndex = 0;
        }

        ResultsListBox.SelectedIndex = nextIndex;
        ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
    }

    private async Task OpenSelectedAsync()
    {
        if (ResultsListBox.SelectedItem is not FileTreeNode node)
        {
            return;
        }

        await _viewModel.OpenFileNodeAsync(node);
        Close();
    }
}
