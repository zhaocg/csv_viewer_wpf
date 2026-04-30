using CsvViewer.Models;
using CsvViewer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CsvViewer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FolderTreeView.ItemContainerGenerator.StatusChanged += FolderTreeViewItemContainerGenerator_StatusChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            _ = Dispatcher.BeginInvoke(ExpandRootTreeItem, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void FolderTreeViewItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (FolderTreeView.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
        {
            ExpandRootTreeItem();
        }
    }

    private void ExpandRootTreeItem()
    {
        if (FolderTreeView.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem rootItem)
        {
            rootItem.IsExpanded = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != System.Windows.Input.ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.P)
        {
            OpenQuickOpenWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.F)
        {
            FocusSearchBox();
            e.Handled = true;
        }
    }

    private void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void OpenQuickOpenWindow()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var quickOpenWindow = new QuickOpenWindow(viewModel)
        {
            Owner = this
        };
        quickOpenWindow.ShowDialog();
    }

    private void QuickOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickOpenWindow();
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await viewModel.LoadFilesAsync(files);
        }
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        GetActiveDocumentGrid()?.CopySelectedCells();
    }

    private void ClearFreeze_Click(object sender, RoutedEventArgs e)
    {
        GetActiveDocumentGrid()?.ClearFreeze();
    }

    private void PaintSelectedCells_Click(object sender, RoutedEventArgs e)
    {
        if (GetActiveDocumentGrid()?.PaintSelectedCells() != true)
        {
            MessageBox.Show("请先选中要刷色的单元格。", "刷色", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SvnLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedDocument.IsRemote: true } viewModel
            || string.IsNullOrWhiteSpace(viewModel.SelectedDocument.FilePath))
        {
            return;
        }

        var logWindow = new SvnLogWindow(viewModel.SelectedDocument.FilePath)
        {
            Owner = this
        };
        logWindow.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var settingsWindow = new SettingsWindow
        {
            Owner = this,
            DataContext = viewModel
        };
        settingsWindow.ShowDialog();
    }

    private async void SvnModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && sender is System.Windows.Controls.Primitives.ToggleButton toggleButton)
        {
            await viewModel.SetSvnModeAsync(toggleButton.IsChecked == true);
        }
    }

    private async void SvnBranchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && sender is ComboBox { SelectedItem: string branch })
        {
            await viewModel.ChangeSvnBranchAsync(branch);
        }
    }

    private void FreezeToCurrentCell_Click(object sender, RoutedEventArgs e)
    {
        if (GetActiveDocumentGrid()?.FreezeToCurrentCell() != true)
        {
            MessageBox.Show("请先选中要冻结到的单元格。", "冻结窗口", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DocumentTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != DocumentTabControl)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => GetActiveDocumentGrid()?.ApplyDocumentFrozenState(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("CSV Viewer\nWindows CSV 只读阅读器", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow
        {
            Owner = this
        };
        helpWindow.ShowDialog();
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
                await viewModel.OpenFileNodeAsync(node);
            }
        }
    }

    private async void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not FileTreeNode node)
        {
            return;
        }

        listBox.SelectedItem = null;
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.OpenFileNodeAsync(node);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private CsvDocumentGrid? GetActiveDocumentGrid()
    {
        if (DataContext is not MainViewModel { SelectedDocument: not null } viewModel)
        {
            return null;
        }

        var container = DocumentContentItemsControl.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedDocument) as DependencyObject;
        if (container == null)
        {
            DocumentContentItemsControl.UpdateLayout();
            container = DocumentContentItemsControl.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedDocument) as DependencyObject;
        }

        return container == null ? null : FindVisualChild<CsvDocumentGrid>(container);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
