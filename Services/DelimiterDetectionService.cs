using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CsvViewer.Services;

public sealed class DelimiterDetectionService
{
    private static readonly string[] Candidates = [",", ";", "\t", "|"];

    public string Detect(string filePath, Encoding encoding)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true);

        while (!reader.EndOfStream && lines.Count < 50)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return ",";
        }

        var scored = Candidates
            .Select(delimiter => new
            {
                Delimiter = delimiter,
                Counts = lines.Select(line => CountFields(line, delimiter[0])).ToArray()
            })
            .Select(item => new
            {
                item.Delimiter,
                Average = item.Counts.Average(),
                Stability = item.Counts.GroupBy(count => count).Max(group => group.Count()),
                Appears = item.Counts.Any(count => count > 1)
            })
            .ToList();

        var active = scored.Where(item => item.Appears).ToList();
        if (active.Count > 0)
        {
            return active
                .OrderByDescending(item => item.Stability)
                .ThenByDescending(item => item.Average)
                .First().Delimiter;
        }

        return ",";
    }

    private static int CountFields(string line, char delimiter)
    {
        var inQuotes = false;
        var count = 1;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && line[i] == delimiter)
            {
                count++;
            }
        }

        return count;
    }
}
