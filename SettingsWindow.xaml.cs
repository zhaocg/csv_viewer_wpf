using System.Windows;
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
}
