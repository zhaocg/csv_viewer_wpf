using System.Collections.Generic;

namespace CsvViewer.Models;

public sealed class AppState
{
    public string? LastFolderPath { get; set; }
    public bool HideColumnHeaders { get; set; }
    public string? SelectedEncoding { get; set; }
    public string? SelectedDelimiter { get; set; }
    public List<string> RecentFilePaths { get; set; } = [];
}
