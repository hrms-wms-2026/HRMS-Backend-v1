using ClosedXML.Excel;
using FluentAssertions;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class XlsxBatchParserTests
{
    private static byte[] BuildWorkbook(string[] headers, IEnumerable<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
                sheet.Cell(rowIndex, col + 1).Value = row[col];
            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Parse_Reads_Headers_And_Rows_From_First_Sheet()
    {
        var bytes = BuildWorkbook(
            ["First Name", "Last Name", "Work Email"],
            [["Jane", "Doe", "jane@acme.test"], ["Bob", "Smith", "bob@acme.test"]]);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Headers.Should().Equal("First Name", "Last Name", "Work Email");
        result.Value.Rows.Should().HaveCount(2);
        result.Value.Rows[0]["First Name"].Should().Be("Jane");
        result.Value.Rows[1]["Work Email"].Should().Be("bob@acme.test");
    }

    [Fact]
    public void Parse_Fails_On_Header_Only_Workbook()
    {
        var bytes = BuildWorkbook(["First Name", "Last Name"], []);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no data rows");
    }

    [Fact]
    public void Parse_Fails_When_Row_Count_Exceeds_MaxRows()
    {
        var tooManyRows = Enumerable.Range(1, CsvBatchParser.MaxRows + 1)
            .Select(i => new[] { $"First{i}", "Doe", $"person{i}@acme.test" });
        var bytes = BuildWorkbook(["First Name", "Last Name", "Work Email"], tooManyRows);

        var result = XlsxBatchParser.Parse(bytes);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(CsvBatchParser.MaxRows.ToString());
    }

    [Fact]
    public void Parse_Fails_On_Corrupt_Bytes()
    {
        var result = XlsxBatchParser.Parse([1, 2, 3, 4, 5]);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Parse_Reads_Date_Cells_As_Human_Readable_Text_Not_Serial_Numbers()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "Start Date";
        sheet.Cell(2, 1).Value = new DateTime(2026, 9, 1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var result = XlsxBatchParser.Parse(stream.ToArray());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows[0]["Start Date"].Should().NotMatchRegex(@"^\d+(\.\d+)?$");
        result.Value.Rows[0]["Start Date"].Should().Contain("2026");
    }
}
