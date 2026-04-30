using CsvViewer.Models;
using CsvViewer.Services;
using System.Windows;

namespace CsvViewer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splashWindow = new SplashWindow();
        IProgress<StartupProgress> progress = new Progress<StartupProgress>(splashWindow.Report);

        progress.Report(new StartupProgress("正在初始化应用资源...", 5));
        splashWindow.Show();

        try
        {
            progress.Report(new StartupProgress("正在创建主窗口...", 25));
            var mainWindow = new MainWindow();

            progress.Report(new StartupProgress("正在恢复上次工作区...", 45));
            await mainWindow.InitializeForStartupAsync(progress);

            progress.Report(new StartupProgress("正在准备界面...", 90));
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            progress.Report(new StartupProgress("启动完成", 100));
            splashWindow.Close();
        }
        catch (Exception ex)
        {
            splashWindow.Close();
            MessageBox.Show($"启动失败: {ex.Message}", "CSV Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SvnFolderService.CleanupCurrentSessionCache();
        base.OnExit(e);
    }
}

