using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public sealed record ParsedCsv(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

/// <summary>
/// Minimal RFC4180-style CSV parser: handles quoted fields, embedded commas inside quotes,
/// and escaped double-quotes ("") inside quoted fields. No external dependency - bulk
/// onboarding's CSVs are flat name/email/date rows, not a general-purpose CSV workload, so a
/// hand-rolled parser is proportionate (see spec §2, CSV-only phase 1 scope).
/// </summary>
public static class CsvBatchParser
{
    public const int MaxRows = 200;

    public static Result<ParsedCsv> Parse(string csvContent)
    {
        var lines = SplitLines(csvContent);
        if (lines.Count == 0)
            return Result<ParsedCsv>.Failure("The file is empty.");

        var headers = SplitLine(lines[0]);
        var dataLines = lines.Skip(1).Where(l => l.Length > 0).ToList();

        if (dataLines.Count == 0)
            return Result<ParsedCsv>.Failure("The file has a header row but no data rows.");

        if (dataLines.Count > MaxRows)
            return Result<ParsedCsv>.Failure($"This file has {dataLines.Count} rows; the limit is {MaxRows} rows per upload.");

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var line in dataLines)
        {
            var values = SplitLine(line);
            var row = new Dictionary<string, string>();
            for (var i = 0; i < headers.Count; i++)
                row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            rows.Add(row);
        }

        return Result<ParsedCsv>.Success(new ParsedCsv(headers, rows));
    }

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') { inQuotes = false; }
                else { current.Append(c); }
            }
            else
            {
                if (c == '"') { inQuotes = true; }
                else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
                else { current.Append(c); }
            }
        }
        fields.Add(current.ToString().Trim());
        return fields;
    }
}
