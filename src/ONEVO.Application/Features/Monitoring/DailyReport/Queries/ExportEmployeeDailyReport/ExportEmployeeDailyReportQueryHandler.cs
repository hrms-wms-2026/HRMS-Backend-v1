using ClosedXML.Excel;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.GetEmployeeDailyReport;

namespace ONEVO.Application.Features.Monitoring.DailyReport.Queries.ExportEmployeeDailyReport;

public class ExportEmployeeDailyReportQueryHandler
    : IRequestHandler<ExportEmployeeDailyReportQuery, Result<DailyReportExportFile>>
{
    private readonly IMediator _mediator;

    public ExportEmployeeDailyReportQueryHandler(IMediator mediator) => _mediator = mediator;

    public async Task<Result<DailyReportExportFile>> Handle(
        ExportEmployeeDailyReportQuery request,
        CancellationToken cancellationToken)
    {
        var reportResult = await _mediator.Send(
            new GetEmployeeDailyReportQuery { EmployeeId = request.EmployeeId, Date = request.Date },
            cancellationToken);

        if (!reportResult.IsSuccess)
            return Result<DailyReportExportFile>.Failure(reportResult.Error!, reportResult.StatusCode ?? 400);

        var report = reportResult.Value!;
        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, report);
        BuildTopAppsSheet(workbook, report);
        BuildScreenshotsSheet(workbook, report);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"daily-report-{report.EmployeeId}-{report.Date:yyyy-MM-dd}.xlsx";
        return Result<DailyReportExportFile>.Success(new DailyReportExportFile(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName));
    }

    private static void BuildSummarySheet(XLWorkbook workbook, EmployeeDailyReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        var rows = new (string Label, string Value)[]
        {
            ("Employee Id", report.EmployeeId.ToString()),
            ("Date", report.Date.ToString("yyyy-MM-dd")),
            ("Clock In (UTC)", report.ClockInAt?.ToString("u") ?? "-"),
            ("Clock Out (UTC)", report.ClockOutAt?.ToString("u") ?? "-"),
            ("Worked Minutes", report.WorkedMinutes.ToString()),
            ("Break Minutes", report.BreakMinutes.ToString()),
            ("Break Count", report.BreakSessionCount.ToString()),
            ("Active Minutes", report.Activity?.TotalActiveMinutes.ToString() ?? "-"),
            ("Idle Minutes", report.Activity?.TotalIdleMinutes.ToString() ?? "-"),
            ("Active %", report.Activity?.ActivePercentage.ToString("0.00") ?? "-"),
            ("Activity Score", report.Activity?.ActivityScore.ToString("0.00") ?? "-"),
            ("Focus Minutes", report.Activity?.FocusMinutes.ToString() ?? "-"),
            ("Deep Focus Sessions", report.Activity?.DeepFocusSessionsCount.ToString() ?? "-"),
            ("Keyboard Events", report.Activity?.KeyboardTotal.ToString() ?? "-"),
            ("Mouse Events", report.Activity?.MouseTotal.ToString() ?? "-"),
            ("Data Coverage %", report.Activity?.DataCoveragePercentage.ToString("0.00") ?? "-"),
            ("Screenshot Count", report.Screenshots.Count.ToString()),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = rows[i].Label;
            sheet.Cell(i + 1, 2).Value = rows[i].Value;
        }

        sheet.Column(1).AdjustToContents();
        sheet.Column(2).AdjustToContents();
    }

    private static void BuildTopAppsSheet(XLWorkbook workbook, EmployeeDailyReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Top Apps");
        sheet.Cell(1, 1).Value = "Rank";
        sheet.Cell(1, 2).Value = "App";
        sheet.Cell(1, 3).Value = "Minutes";

        var topApps = report.Activity?.TopApps ?? [];
        for (var i = 0; i < topApps.Count; i++)
        {
            sheet.Cell(i + 2, 1).Value = i + 1;
            sheet.Cell(i + 2, 2).Value = topApps[i].AppName;
            sheet.Cell(i + 2, 3).Value = topApps[i].TotalSeconds / 60;
        }

        sheet.Column(1).AdjustToContents();
        sheet.Column(2).AdjustToContents();
        sheet.Column(3).AdjustToContents();
    }

    private static void BuildScreenshotsSheet(XLWorkbook workbook, EmployeeDailyReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Screenshots");
        sheet.Cell(1, 1).Value = "Captured At (UTC)";
        sheet.Cell(1, 2).Value = "Type";
        sheet.Cell(1, 3).Value = "Trigger";

        for (var i = 0; i < report.Screenshots.Count; i++)
        {
            var shot = report.Screenshots[i];
            sheet.Cell(i + 2, 1).Value = shot.CapturedAt.ToString("u");
            sheet.Cell(i + 2, 2).Value = shot.EvidenceType;
            sheet.Cell(i + 2, 3).Value = shot.TriggerType;
        }

        sheet.Column(1).AdjustToContents();
        sheet.Column(2).AdjustToContents();
        sheet.Column(3).AdjustToContents();
    }
}
