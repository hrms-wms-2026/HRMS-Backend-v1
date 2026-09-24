using ClosedXML.Excel;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;

public sealed class GetBulkOnboardingTemplateQueryHandler
{
    public Result<BulkOnboardingTemplateFile> Handle(GetBulkOnboardingTemplateQuery request)
    {
        return request.Format.ToLowerInvariant() switch
        {
            "csv" => Result<BulkOnboardingTemplateFile>.Success(BuildCsv()),
            "xlsx" => Result<BulkOnboardingTemplateFile>.Success(BuildXlsx()),
            _ => Result<BulkOnboardingTemplateFile>.Failure("Unsupported template format. Use 'csv' or 'xlsx'."),
        };
    }

    private static BulkOnboardingTemplateFile BuildCsv()
    {
        var fields = BulkOnboardingTemplateFields.All;
        var header = string.Join(",", fields.Select(f => f.Label));
        var exampleRow = string.Join(",", fields.Select(f => f.ExampleValue));
        var content = System.Text.Encoding.UTF8.GetBytes($"{header}\n{exampleRow}\n");

        return new BulkOnboardingTemplateFile(content, "text/csv", "bulk-onboarding-template.csv");
    }

    private static BulkOnboardingTemplateFile BuildXlsx()
    {
        var fields = BulkOnboardingTemplateFields.All;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");

        for (var i = 0; i < fields.Count; i++)
            sheet.Cell(1, i + 1).Value = fields[i].Label;

        for (var i = 0; i < fields.Count; i++)
            sheet.Cell(2, i + 1).Value = fields[i].ExampleValue;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return new BulkOnboardingTemplateFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "bulk-onboarding-template.xlsx");
    }
}
