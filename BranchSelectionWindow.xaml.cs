using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace CsvViewer;

public partial class BranchSelectionWindow : Window
{
    public BranchSelectionWindow(IEnumerable<string> branchNames)
    {
        InitializeComponent();
        Branches = new ObservableCollection<SelectableBranch>(branchNames.Select(branch => new SelectableBranch(branch)));
        DataContext = this;
    }

    public ObservableCollection<SelectableBranch> Branches { get; }

    public IReadOnlyList<string> SelectedBranches => Branches
        .Where(branch => branch.IsSelected)
        .Select(branch => branch.Name)
        .ToArray();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBranches.Count == 0)
        {
            MessageBox.Show("请至少选择一个分支。", "选择 SVN 分支", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    public sealed class SelectableBranch(string name)
    {
        public string Name { get; } = name;

        public bool IsSelected { get; set; }
    }
}
