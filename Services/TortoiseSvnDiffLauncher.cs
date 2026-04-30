using System;
using System.Diagnostics;
using System.IO;

namespace CsvViewer.Services;

public sealed class TortoiseSvnDiffLauncher
{
    public bool IsAvailable => FindTortoiseProcPath() is not null;

    public static string BuildDiffArguments(string leftPath, string rightPath)
    {
        if (Uri.TryCreate(leftPath, UriKind.Absolute, out var leftUri) && !leftUri.IsFile)
        {
            throw new ArgumentException("TortoiseSVN 比较需要本地文件路径。", nameof(leftPath));
        }

        if (Uri.TryCreate(rightPath, UriKind.Absolute, out var rightUri) && !rightUri.IsFile)
        {
            throw new ArgumentException("TortoiseSVN 比较需要本地文件路径。", nameof(rightPath));
        }

        return $"/command:diff /path:\"{leftPath}\" /path2:\"{rightPath}\"";
    }

    public void LaunchDiff(string leftPath, string rightPath)
    {
        var executablePath = FindTortoiseProcPath();
        if (executablePath is null)
        {
            throw new FileNotFoundException("未找到 TortoiseSVN。请确认已安装 TortoiseSVN，并且 TortoiseProc.exe 位于 PATH 或默认安装目录。", "TortoiseProc.exe");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = BuildDiffArguments(leftPath, rightPath),
            UseShellExecute = false
        });
    }

    private static string? FindTortoiseProcPath()
    {
        return FindTortoiseProcPath(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    }

    public static string? FindTortoiseProcPath(string pathValue, string programFiles, string programFilesX86)
    {
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "TortoiseProc.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var defaultCandidates = new[]
        {
            Path.Combine(programFiles, "TortoiseSVN", "bin", "TortoiseProc.exe"),
            Path.Combine(programFilesX86, "TortoiseSVN", "bin", "TortoiseProc.exe")
        };

        return defaultCandidates.FirstOrDefault(File.Exists);
    }
}
