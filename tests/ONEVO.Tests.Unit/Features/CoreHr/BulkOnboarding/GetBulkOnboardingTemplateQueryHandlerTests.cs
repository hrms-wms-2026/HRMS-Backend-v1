using ClosedXML.Excel;
using FluentAssertions;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class GetBulkOnboardingTemplateQueryHandlerTests
{
    private readonly GetBulkOnboardingTemplateQueryHandler _handler = new();

    [Fact]
    public void Handle_Csv_Includes_All_Field_Labels_In_Order()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("csv"));

        result.IsSuccess.Should().BeTrue();
        var text = System.Text.Encoding.UTF8.GetString(result.Value!.Content);
        var firstLine = text.Split('\n')[0].TrimEnd('\r');
        firstLine.Should().Be("First Name,Last Name,Work Email,Start Date,Employment Type,Work Mode,Department,Position,Checklist Template,Employee Number,Reporting Manager");
    }

    [Fact]
    public void Handle_Csv_Leaves_Tenant_Specific_Fields_Blank_In_Example_Row()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("csv"));

        var text = System.Text.Encoding.UTF8.GetString(result.Value!.Content);
        var lines = text.Split('\n');
        var exampleRow = lines[1].TrimEnd('\r').Split(',');
        exampleRow[0].Should().Be("Jane");
        exampleRow[5].Should().BeEmpty();
        exampleRow[6].Should().BeEmpty();
        exampleRow[10].Should().BeEmpty();
    }

    [Fact]
    public void Handle_Xlsx_Produces_A_Readable_Workbook_With_The_Same_Headers()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("xlsx"));

        result.IsSuccess.Should().BeTrue();
        using var stream = new MemoryStream(result.Value!.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        sheet.Cell(1, 1).GetString().Should().Be("First Name");
        sheet.Cell(1, 11).GetString().Should().Be("Reporting Manager");
        sheet.Cell(2, 1).GetString().Should().Be("Jane");
    }

    [Fact]
    public void Handle_Returns_Failure_For_Unsupported_Format()
    {
        var result = _handler.Handle(new GetBulkOnboardingTemplateQuery("pdf"));

        result.IsSuccess.Should().BeFalse();
    }
}
