using CsvViewer.Models;
using System.Windows;

namespace CsvViewer;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void Report(StartupProgress progress)
    {
        var percent = Math.Clamp(progress.Percent, 0, 100);
        ProgressTextBlock.Text = progress.Message;
        StartupProgressBar.Value = percent;
        PercentTextBlock.Text = $"{percent}%";
    }
}
