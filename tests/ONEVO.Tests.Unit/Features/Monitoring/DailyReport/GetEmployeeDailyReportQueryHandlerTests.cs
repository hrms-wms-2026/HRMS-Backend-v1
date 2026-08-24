using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DailyReport.Queries.GetEmployeeDailyReport;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.DailyReport;

public sealed class GetEmployeeDailyReportQueryHandlerTests
{
    private readonly Mock<IActivityDailySummaryRepository> _summaries = new();
    private readonly Mock<IWorkSessionRepository> _workSessions = new();
    private readonly Mock<IEvidenceAssetRepository> _evidenceAssets = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private GetEmployeeDailyReportQueryHandler CreateHandler() => new(
        _summaries.Object, _workSessions.Object, _evidenceAssets.Object,
        _employees.Object, _fileStorage.Object, _tenantContext.Object);

    [Fact]
    public async Task Handle_Combines_WorkSession_Activity_And_Screenshots()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 19);

        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);
        _employees.Setup(e => e.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, TenantId = tenantId, UserId = userId });

        _summaries.Setup(s => s.GetAsync(tenantId, employeeId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities.ActivityDailySummary?)null);

        var clockIn = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
        var clockOut = new DateTimeOffset(2026, 8, 19, 17, 30, 0, TimeSpan.Zero);
        _workSessions.Setup(w => w.GetForUserAndDateAsync(tenantId, userId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeWorkSession>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    ClockInAt = clockIn,
                    ClockOutAt = clockOut,
                    AccumulatedBreakSeconds = 1800,
                    AccumulatedWorkSeconds = 28800,
                    BreakSessionCount = 2
                }
            });

        var fileRecordId = Guid.NewGuid();
        _evidenceAssets.Setup(a => a.GetPagedAsync(
                tenantId, employeeId, date, date, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<MonitoringEvidenceAsset>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    FileRecordId = fileRecordId,
                    EvidenceType = "screenshot",
                    TriggerType = "periodic",
                    CapturedAt = clockIn.AddHours(1)
                }
            }, 1));

        _fileStorage.Setup(f => f.GetSignedUrlAsync(
                tenantId, fileRecordId, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed-url.example/shot.png"));

        var result = await CreateHandler().Handle(
            new GetEmployeeDailyReportQuery { EmployeeId = employeeId, Date = date }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.ClockInAt.Should().Be(clockIn);
        report.ClockOutAt.Should().Be(clockOut);
        report.BreakMinutes.Should().Be(30);
        report.BreakSessionCount.Should().Be(2);
        report.WorkedMinutes.Should().Be(480);
        report.Activity.Should().BeNull();
        report.Screenshots.Should().ContainSingle();
        report.Screenshots[0].Url.Should().Be("https://signed-url.example/shot.png");
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_Employee_Missing()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _tenantContext.Setup(t => t.TenantId).Returns(tenantId);
        _employees.Setup(e => e.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await CreateHandler().Handle(
            new GetEmployeeDailyReportQuery { EmployeeId = employeeId, Date = new DateOnly(2026, 8, 19) },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Returns_Unauthorized_When_No_Tenant_Context()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(Guid.Empty);

        var result = await CreateHandler().Handle(
            new GetEmployeeDailyReportQuery { EmployeeId = Guid.NewGuid(), Date = new DateOnly(2026, 8, 19) },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
