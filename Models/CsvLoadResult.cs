using System.Data;
using System.Text;

namespace CsvViewer.Models;

public sealed class CsvLoadResult
{
    public required DataTable Table { get; init; }
    public required string FilePath { get; init; }
    public required Encoding Encoding { get; init; }
    public required string Delimiter { get; init; }
    public required long FileSize { get; init; }
    public required bool HasHeader { get; init; }
}
