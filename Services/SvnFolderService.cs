using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CsvViewer.Models;

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

    public async Task<IReadOnlyList<string>> ListDirectoriesAsync(string svnUrl)
    {
        if (string.IsNullOrWhiteSpace(svnUrl))
        {
            throw new ArgumentException("请输入 SVN 文件夹链接。", nameof(svnUrl));
        }

        var output = await RunSvnTextAsync(["list", svnUrl.Trim(), "--non-interactive"]);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.EndsWith('/'))
            .Select(path => path.Trim('/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
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

    public async Task<IReadOnlyList<SvnLogEntry>> GetLogAsync(string svnFileUrl, int limit, long? startRevision = null)
    {
        if (string.IsNullOrWhiteSpace(svnFileUrl))
        {
            throw new ArgumentException("SVN 文件链接为空。", nameof(svnFileUrl));
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "日志条数必须大于 0。");
        }

        var arguments = new List<string> { "log", svnFileUrl.Trim(), "-v", "-l", limit.ToString(), "--non-interactive" };
        if (startRevision is > 0)
        {
            arguments.Add("-r");
            arguments.Add($"{startRevision}:1");
        }

        var output = await RunSvnTextAsync(arguments.ToArray());
        return ParseTextLogEntries(output);
    }

    public static string CombineUrl(string rootUrl, string relativePath)
    {
        return $"{rootUrl.TrimEnd('/')}/{relativePath.Replace('\\', '/').TrimStart('/')}";
    }

    private static IReadOnlyList<SvnLogEntry> ParseTextLogEntries(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var entries = new List<SvnLogEntry>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = normalized.Split("------------------------------------------------------------------------", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length == 0)
            {
                continue;
            }

            var headerParts = lines[0].Split('|', 4, StringSplitOptions.TrimEntries);
            if (headerParts.Length < 3 || !long.TryParse(headerParts[0].TrimStart('r'), out var revision))
            {
                continue;
            }

            var changedPaths = new List<string>();
            var messageLines = new List<string>();
            var readingPaths = false;
            var readingMessage = false;

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("Changed paths:", StringComparison.OrdinalIgnoreCase))
                {
                    readingPaths = true;
                    continue;
                }

                if (readingPaths)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        readingPaths = false;
                        readingMessage = true;
                        continue;
                    }

                    changedPaths.Add(line.Trim());
                    continue;
                }

                if (readingMessage || !string.IsNullOrWhiteSpace(line))
                {
                    readingMessage = true;
                    messageLines.Add(line);
                }
            }

            entries.Add(new SvnLogEntry
            {
                Revision = revision,
                Author = headerParts[1],
                Date = ParseSvnLogDate(headerParts[2]),
                Message = string.Join(Environment.NewLine, messageLines).Trim(),
                ChangedPaths = changedPaths
            });
        }

        return entries.OrderByDescending(entry => entry.Revision).ToArray();
    }

    private static DateTimeOffset ParseSvnLogDate(string value)
    {
        var dateText = value;
        var parenthesisIndex = dateText.IndexOf('(');
        if (parenthesisIndex >= 0)
        {
            dateText = dateText[..parenthesisIndex].Trim();
        }

        return DateTimeOffset.TryParse(dateText, out var date) ? date : DateTimeOffset.MinValue;
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
