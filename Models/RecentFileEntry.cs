namespace CsvViewer.Models;

public sealed class RecentFileEntry
{
    public bool IsRemote { get; set; }
    public string Path { get; set; } = string.Empty;
}
