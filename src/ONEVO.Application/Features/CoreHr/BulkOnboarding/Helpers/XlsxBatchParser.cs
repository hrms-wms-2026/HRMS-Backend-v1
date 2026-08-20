using ClosedXML.Excel;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public static class XlsxBatchParser
{
    public static Result<ParsedBatchFile> Parse(byte[] fileContent)
    {
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();

            var usedRange = sheet.RangeUsed();
            if (usedRange is null)
                return Result<ParsedBatchFile>.Failure("The file is empty.");

            var firstRow = usedRange.FirstRow();
            var headers = firstRow.Cells()
                .Select(c => c.GetString().Trim())
                .Where(h => h.Length > 0)
                .ToList();

            if (headers.Count == 0)
                return Result<ParsedBatchFile>.Failure("The file is empty.");

            var dataRows = usedRange.Rows()
                .Skip(1)
                .Where(r => r.Cells().Any(c => !string.IsNullOrWhiteSpace(c.GetString())))
                .ToList();

            if (dataRows.Count == 0)
                return Result<ParsedBatchFile>.Failure("The file has a header row but no data rows.");

            if (dataRows.Count > CsvBatchParser.MaxRows)
                return Result<ParsedBatchFile>.Failure(
                    $"This file has {dataRows.Count} rows; the limit is {CsvBatchParser.MaxRows} rows per upload.");

            var rows = new List<IReadOnlyDictionary<string, string>>();
            foreach (var dataRow in dataRows)
            {
                var row = new Dictionary<string, string>();
                for (var i = 0; i < headers.Count; i++)
                {
                    var cell = dataRow.Cell(i + 1);
                    row[headers[i]] = cell.GetString().Trim();
                }
                rows.Add(row);
            }

            return Result<ParsedBatchFile>.Success(new ParsedBatchFile(headers, rows));
        }
        catch (Exception)
        {
            return Result<ParsedBatchFile>.Failure("Could not read this file as an Excel workbook.");
        }
    }
}
