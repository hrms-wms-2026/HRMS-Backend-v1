using ClosedXML.Excel;
using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.ExportEmployeeDailyReport;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.GetEmployeeDailyReport;

namespace ONEVO.Tests.Unit.Features.Monitoring.DailyReport;

public sealed class ExportEmployeeDailyReportQueryHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();

    private ExportEmployeeDailyReportQueryHandler CreateHandler() => new(_mediator.Object);

    [Fact]
    public async Task Handle_Builds_Workbook_With_Summary_TopApps_And_Screenshots_Sheets()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 19);
        var clockIn = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
        var clockOut = new DateTimeOffset(2026, 8, 19, 17, 30, 0, TimeSpan.Zero);

        var report = new EmployeeDailyReportDto
        {
            EmployeeId = employeeId,
            Date = date,
            ClockInAt = clockIn,
            ClockOutAt = clockOut,
            WorkedMinutes = 480,
            BreakMinutes = 30,
            BreakSessionCount = 2,
            Activity = new ActivityDailySummaryDto
            {
                EmployeeId = employeeId,
                Date = date,
                TotalActiveMinutes = 420,
                TotalIdleMinutes = 60,
                ActivePercentage = 87.5m,
                ActivityScore = 72.3m,
                FocusMinutes = 180,
                DeepFocusSessionsCount = 2,
                KeyboardTotal = 5000,
                MouseTotal = 3000,
                DataCoveragePercentage = 100m,
                TopApps = [new AppUsageSummary { AppName = "code.exe", TotalSeconds = 7200, Category = "" }]
            },
            Screenshots =
            [
                new ScreenshotEntryDto(Guid.NewGuid(), clockIn.AddHours(1), "screenshot", "periodic", "https://x/1.png")
            ]
        };

        _mediator.Setup(m => m.Send(
                It.Is<GetEmployeeDailyReportQuery>(q => q.EmployeeId == employeeId && q.Date == date),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeDailyReportDto>.Success(report));

        var result = await CreateHandler().Handle(
            new ExportEmployeeDailyReportQuery { EmployeeId = employeeId, Date = date }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var file = result.Value!;
        file.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.FileName.Should().Be($"daily-report-{employeeId}-2026-08-19.xlsx");

        using var stream = new MemoryStream(file.Content);
        using var workbook = new XLWorkbook(stream);
        workbook.Worksheets.Select(w => w.Name).Should().BeEquivalentTo("Summary", "Top Apps", "Screenshots");

        var summary = workbook.Worksheet("Summary");
        summary.Cell(1, 1).GetString().Should().Be("Employee Id");
        summary.Cell(1, 2).GetString().Should().Be(employeeId.ToString());

        var topApps = workbook.Worksheet("Top Apps");
        topApps.Cell(2, 2).GetString().Should().Be("code.exe");
        topApps.Cell(2, 3).GetValue<int>().Should().Be(120); // 7200s / 60

        var screenshots = workbook.Worksheet("Screenshots");
        screenshots.Cell(2, 2).GetString().Should().Be("screenshot");
    }

    [Fact]
    public async Task Handle_Propagates_Failure_From_Inner_Query()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetEmployeeDailyReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeDailyReportDto>.NotFound("Employee not found."));

        var result = await CreateHandler().Handle(
            new ExportEmployeeDailyReportQuery { EmployeeId = Guid.NewGuid(), Date = new DateOnly(2026, 8, 19) },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
