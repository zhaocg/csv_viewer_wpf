using System.Collections.Generic;

namespace CsvViewer.Models;

public sealed class AppState
{
    public string? LastFolderPath { get; set; }
    public bool HideColumnHeaders { get; set; }
    public string? SelectedEncoding { get; set; }
    public string? SelectedDelimiter { get; set; }
    public string? LastSvnUrl { get; set; }
    public bool IsSvnMode { get; set; }
    public string? SvnExcelPathTemplate { get; set; }
    public string? SelectedSvnBranch { get; set; }
    public List<string> SvnBranches { get; set; } = [];
    public List<RecentFileEntry> RecentFiles { get; set; } = [];
    public List<string> RecentFilePaths { get; set; } = [];
}
