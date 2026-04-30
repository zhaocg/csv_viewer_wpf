using CsvViewer.Services;

var arguments = TortoiseSvnDiffLauncher.BuildDiffArguments(
    @"C:\Temp\CsvViewer\Compare\main\file.csv",
    @"C:\Temp\CsvViewer\Compare\dev\file.csv");

AssertEqual("/command:diff /path:\"C:\\Temp\\CsvViewer\\Compare\\main\\file.csv\" /path2:\"C:\\Temp\\CsvViewer\\Compare\\dev\\file.csv\"", arguments);
AssertThrows<ArgumentException>(() => TortoiseSvnDiffLauncher.BuildDiffArguments(
    "svn://server/branches/main/data/Excel/file.csv",
    @"C:\Temp\CsvViewer\Compare\dev\file.csv"));

var tortoisePathRoot = Path.Combine(Path.GetTempPath(), "CsvViewerTests", Guid.NewGuid().ToString("N"), "TortoiseSVN", "bin");
Directory.CreateDirectory(tortoisePathRoot);
var tortoiseProcPath = Path.Combine(tortoisePathRoot, "TortoiseProc.exe");
File.WriteAllText(tortoiseProcPath, string.Empty);
AssertEqual(tortoiseProcPath, TortoiseSvnDiffLauncher.FindTortoiseProcPath(tortoisePathRoot, string.Empty, string.Empty) ?? string.Empty);
AssertEqual(string.Empty, TortoiseSvnDiffLauncher.FindTortoiseProcPath(string.Empty, string.Empty, string.Empty) ?? string.Empty);

var cacheRoot = Path.Combine(Path.GetTempPath(), "CsvViewerTests", Guid.NewGuid().ToString("N"), "SvnFileCache");
var oldSession = Path.Combine(cacheRoot, "old-session");
var currentSession = Path.Combine(cacheRoot, "current-session");
Directory.CreateDirectory(oldSession);
Directory.CreateDirectory(currentSession);
File.WriteAllText(Path.Combine(oldSession, "old.csv"), "old");
File.WriteAllText(Path.Combine(currentSession, "current.csv"), "current");

SvnFolderService.CleanupOldSessionCaches(cacheRoot, "current-session");
AssertFalse(Directory.Exists(oldSession), "Old session cache should be removed on startup.");
AssertTrue(Directory.Exists(currentSession), "Current session cache should not be removed on startup.");

SvnFolderService.CleanupSessionCache(cacheRoot, "current-session");
AssertFalse(Directory.Exists(currentSession), "Current session cache should be removed on exit.");

static void AssertEqual(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
