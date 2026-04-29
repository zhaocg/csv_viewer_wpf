using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CsvViewer.Services;

public sealed class SvnFolderService
{
    private readonly string _legacyCheckoutCacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CsvViewer",
        "SvnFolders");

    private readonly string _sessionCacheRoot = Path.Combine(
        Path.GetTempPath(),
        "CsvViewer",
        "SvnFileCache",
        Environment.ProcessId.ToString());

    public SvnFolderService()
    {
        DeleteLegacyCheckoutCache();
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string svnUrl)
    {
        if (string.IsNullOrWhiteSpace(svnUrl))
        {
            throw new ArgumentException("请输入 SVN 文件夹链接。", nameof(svnUrl));
        }

        var output = await RunSvnTextAsync(["list", "-R", svnUrl.Trim(), "--non-interactive"]);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !path.EndsWith('/'))
            .ToArray();
    }

    public async Task<string> CacheFileAsync(string svnFileUrl, string fileName)
    {
        if (string.IsNullOrWhiteSpace(svnFileUrl))
        {
            throw new ArgumentException("SVN 文件链接为空。", nameof(svnFileUrl));
        }

        Directory.CreateDirectory(_sessionCacheRoot);
        var extension = Path.GetExtension(fileName);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svnFileUrl)))[..16];
        var localPath = Path.Combine(_sessionCacheRoot, $"{hash}{extension}");

        await RunSvnToFileAsync(["cat", svnFileUrl.Trim(), "--non-interactive"], localPath);
        return localPath;
    }

    public static string CombineUrl(string rootUrl, string relativePath)
    {
        return $"{rootUrl.TrimEnd('/')}/{relativePath.Replace('\\', '/').TrimStart('/')}";
    }

    private static async Task<string> RunSvnTextAsync(string[] arguments)
    {
        using var process = CreateSvnProcess(arguments, redirectStandardOutput: true);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("未找到 svn 命令行客户端，请先安装 SVN 并确保 svn.exe 已加入 PATH。", ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException($"SVN 操作失败。{Environment.NewLine}{message.Trim()}");
        }

        return output;
    }

    private static async Task RunSvnToFileAsync(string[] arguments, string localPath)
    {
        using var process = CreateSvnProcess(arguments, redirectStandardOutput: true);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("未找到 svn 命令行客户端，请先安装 SVN 并确保 svn.exe 已加入 PATH。", ex);
        }

        await using var outputFile = File.Create(localPath);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(outputFile);
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await copyTask;

        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            outputFile.Close();
            File.Delete(localPath);
            throw new InvalidOperationException($"SVN 操作失败。{Environment.NewLine}{error.Trim()}");
        }
    }

    private static Process CreateSvnProcess(string[] arguments, bool redirectStandardOutput)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "svn",
                UseShellExecute = false,
                RedirectStandardOutput = redirectStandardOutput,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private void DeleteLegacyCheckoutCache()
    {
        try
        {
            if (Directory.Exists(_legacyCheckoutCacheRoot))
            {
                Directory.Delete(_legacyCheckoutCacheRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
