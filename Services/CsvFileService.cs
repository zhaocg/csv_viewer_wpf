using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using CsvViewer.Models;

namespace CsvViewer.Services;

public sealed class CsvFileService
{
    private readonly EncodingDetectionService _encodingDetectionService = new();
    private readonly DelimiterDetectionService _delimiterDetectionService = new();

    public CsvLoadResult Load(string filePath, Encoding? forcedEncoding = null, string? forcedDelimiter = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件不存在。", filePath);
        }

        var encoding = forcedEncoding ?? _encodingDetectionService.Detect(filePath);
        var delimiter = string.IsNullOrEmpty(forcedDelimiter) ? _delimiterDetectionService.Detect(filePath, encoding) : forcedDelimiter;
        var rows = ReadRows(filePath, encoding, delimiter);
        var table = BuildDataTable(rows, out var hasHeader);

        return new CsvLoadResult
        {
            Table = table,
            FilePath = filePath,
            Encoding = encoding,
            Delimiter = delimiter,
            FileSize = new FileInfo(filePath).Length,
            HasHeader = hasHeader
        };
    }

    private static List<string[]> ReadRows(string filePath, Encoding encoding, string delimiter)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false
        });

        while (csv.Read())
        {
            rows.Add(csv.Parser.Record ?? []);
        }

        return rows;
    }

    private static DataTable BuildDataTable(IReadOnlyList<string[]> rows, out bool hasHeader)
    {
        var table = new DataTable();
        hasHeader = rows.Count > 0 && LooksLikeHeader(rows[0]);
        var maxColumns = rows.Count == 0 ? 0 : rows.Max(row => row.Length);

        if (maxColumns == 0)
        {
            return table;
        }

        var headers = hasHeader ? rows[0] : Enumerable.Range(0, maxColumns).Select(GetExcelColumnName).ToArray();
        for (var i = 0; i < maxColumns; i++)
        {
            var header = i < headers.Length && !string.IsNullOrWhiteSpace(headers[i]) ? headers[i].Trim() : GetExcelColumnName(i);
            table.Columns.Add(MakeUniqueColumnName(table, header), typeof(string));
        }

        var startIndex = hasHeader ? 1 : 0;
        for (var rowIndex = startIndex; rowIndex < rows.Count; rowIndex++)
        {
            var source = rows[rowIndex];
            var row = table.NewRow();
            for (var columnIndex = 0; columnIndex < maxColumns; columnIndex++)
            {
                row[columnIndex] = columnIndex < source.Length ? source[columnIndex] : string.Empty;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static bool LooksLikeHeader(string[] row)
    {
        if (row.Length == 0 || row.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        return row.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == row.Length;
    }

    private static string GetExcelColumnName(int zeroBasedIndex)
    {
        var dividend = zeroBasedIndex + 1;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = (char)('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string MakeUniqueColumnName(DataTable table, string baseName)
    {
        var name = baseName;
        var index = 2;

        while (table.Columns.Contains(name))
        {
            name = $"{baseName} ({index++})";
        }

        return name;
    }
}
