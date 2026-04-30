using System.Windows;
using System.Windows.Input;
using CsvViewer.ViewModels;

namespace CsvViewer;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.RefreshSvnModeAsync();
        }

        Close();
    }

    private async void DiscoverSvnBranches_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var branchNames = await viewModel.DiscoverSvnBranchesAsync();
            if (branchNames.Count == 0)
            {
                MessageBox.Show("没有发现可新增的 SVN 分支。", "分支预设", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectionWindow = new BranchSelectionWindow(branchNames)
            {
                Owner = this
            };

            if (selectionWindow.ShowDialog() == true)
            {
                viewModel.AddSvnBranches(selectionWindow.SelectedBranches);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"获取 SVN 分支失败: {ex.Message}", "分支预设", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
