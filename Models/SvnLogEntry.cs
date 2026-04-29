using System;
using System.Collections.Generic;
using System.Linq;

namespace CsvViewer.Models;

public sealed class SvnLogEntry
{
    public long Revision { get; init; }
    public string Author { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];

    public string RevisionText => $"r{Revision}";
    public string DateText => Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ChangedPathsText => ChangedPaths.Count == 0 ? "-" : string.Join(Environment.NewLine, ChangedPaths.Take(8));
}
