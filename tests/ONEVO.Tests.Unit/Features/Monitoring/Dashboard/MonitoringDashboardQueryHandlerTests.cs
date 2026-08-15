using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;
using ONEVO.Application.Features.Monitoring.Dashboard.Queries.GetMonitoringDashboard;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Dashboard;

public class MonitoringDashboardQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly DateOnly _date = new(2026, 8, 14);
    private readonly DateTimeOffset _now = new(2026, 8, 14, 9, 5, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_maps_visible_employee_monitoring_status_summary_and_alerts()
    {
        var currentUser = CurrentUser(canManageOrg: true);
        var employees = new Mock<IEmployeeRepository>();
        employees
            .Setup(r => r.ListVisibleAsync(
                _tenantId,
                It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees),
                It.Is<EmployeeListFilter>(f => f.Search == "pirakee"),
                1,
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                [new EmployeeListItemResponse(
                    _employeeId,
                    "EMP-001",
                    "Pirakee Dev",
                    "pirakee@example.com",
                    DepartmentId: Guid.NewGuid(),
                    DepartmentName: "Engineering",
                    PositionId: Guid.NewGuid(),
                    PositionName: "Developer",
                    LegalEntityId: Guid.NewGuid(),
                    LegalEntityName: "ONEVO",
                    EmploymentTypeLabel: "Full Time",
                    Status: "active",
                    ReportingManagerId: null,
                    ReportingManagerName: null)],
                1));

        var summaries = new Mock<IActivityDailySummaryRepository>();
        summaries
            .Setup(r => r.GetAsync(_tenantId, _employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummary
            {
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                Date = _date,
                TotalActiveMinutes = 360,
                TotalIdleMinutes = 30,
                ActivityScore = 87,
                DataCoveragePercentage = 92,
                TopAppsJson = """[{"appName":"Code.exe","totalSeconds":3600,"category":"productive"}]"""
            });

        var deviceStates = new Mock<IDeviceStateSnapshotRepository>();
        deviceStates
            .Setup(r => r.GetLatestForEmployeesAsync(
                _tenantId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { _employeeId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DeviceStateSnapshot>
            {
                [_employeeId] = new()
                {
                    TenantId = _tenantId,
                    EmployeeId = _employeeId,
                    CapturedAt = _now.AddMinutes(-2),
                    IsIdle = false
                }
            });

        var workSessions = new Mock<IWorkSessionRepository>();
        workSessions
            .Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId,
                _employeeId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new EmployeeWorkSession
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ClockInAt = new DateTimeOffset(2026, 8, 14, 9, 11, 0, TimeSpan.Zero),
                    ClockOutAt = new DateTimeOffset(2026, 8, 14, 18, 0, 0, TimeSpan.Zero),
                    AccumulatedWorkSeconds = 8 * 60 * 60,
                    AccumulatedBreakSeconds = 60 * 60,
                    BreakSessionCount = 1
                }
            ]);

        var handler = new GetMonitoringDashboardQueryHandler(
            employees.Object,
            Mock.Of<IEmployeeVisibilityScopeResolver>(),
            currentUser.Object,
            summaries.Object,
            deviceStates.Object,
            workSessions.Object,
            Mock.Of<IMonitoringReportTimeZoneResolver>(r =>
                r.ResolveAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()) == Task.FromResult(TimeZoneInfo.Utc)),
            Mock.Of<IDateTimeProvider>(c => c.UtcNow == _now));

        var result = await handler.Handle(
            new GetMonitoringDashboardQuery(
                Date: _date,
                Search: "pirakee",
                DepartmentId: null,
                LegalEntityId: null,
                Page: 1,
                PageSize: 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Summary.ActiveEmployees.Should().Be(1);
        result.Value.Summary.AverageActivityScore.Should().Be(87);
        result.Value.Employees.Should().ContainSingle();

        var employee = result.Value.Employees[0];
        employee.EmployeeId.Should().Be(_employeeId);
        employee.Status.Should().Be(MonitoringEmployeeStatus.Active);
        employee.ActivityScore.Should().Be(87);
        employee.TopApps.Should().ContainSingle(a => a.AppName == "Code.exe");
        employee.Alerts.Should().ContainSingle(a => a.Code == "late_login");
    }

    [Fact]
    public async Task Handle_uses_resolved_time_zone_for_shift_alerts()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Sri Lanka Test",
            TimeSpan.FromMinutes(330),
            "Sri Lanka Test",
            "Sri Lanka Test");

        var currentUser = CurrentUser(canManageOrg: true);
        var employees = new Mock<IEmployeeRepository>();
        employees
            .Setup(r => r.ListVisibleAsync(
                _tenantId,
                It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees),
                It.IsAny<EmployeeListFilter>(),
                1,
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                [new EmployeeListItemResponse(
                    _employeeId,
                    "EMP-001",
                    "Pirakee Dev",
                    "pirakee@example.com",
                    DepartmentId: null,
                    DepartmentName: null,
                    PositionId: null,
                    PositionName: null,
                    LegalEntityId: null,
                    LegalEntityName: null,
                    EmploymentTypeLabel: "Full Time",
                    Status: "active",
                    ReportingManagerId: null,
                    ReportingManagerName: null)],
                1));

        var summaries = new Mock<IActivityDailySummaryRepository>();
        summaries
            .Setup(r => r.GetAsync(_tenantId, _employeeId, _date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummary
            {
                TenantId = _tenantId,
                EmployeeId = _employeeId,
                Date = _date,
                TotalActiveMinutes = 420,
                TotalIdleMinutes = 30,
                ActivityScore = 80,
                DataCoveragePercentage = 90,
                TopAppsJson = "[]"
            });

        var deviceStates = new Mock<IDeviceStateSnapshotRepository>();
        deviceStates
            .Setup(r => r.GetLatestForEmployeesAsync(
                _tenantId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DeviceStateSnapshot>());

        var workSessions = new Mock<IWorkSessionRepository>();
        workSessions
            .Setup(r => r.GetByEmployeeRangeAsync(
                _tenantId,
                _employeeId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new EmployeeWorkSession
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    ClockInAt = new DateTimeOffset(2026, 8, 14, 3, 30, 0, TimeSpan.Zero),
                    ClockOutAt = new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero),
                    AccumulatedWorkSeconds = 8 * 60 * 60,
                    AccumulatedBreakSeconds = 60 * 60,
                    BreakSessionCount = 1
                }
            ]);

        var handler = new GetMonitoringDashboardQueryHandler(
            employees.Object,
            Mock.Of<IEmployeeVisibilityScopeResolver>(),
            currentUser.Object,
            summaries.Object,
            deviceStates.Object,
            workSessions.Object,
            Mock.Of<IMonitoringReportTimeZoneResolver>(r =>
                r.ResolveAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()) == Task.FromResult(timeZone)),
            Mock.Of<IDateTimeProvider>(c => c.UtcNow == _now));

        var result = await handler.Handle(
            new GetMonitoringDashboardQuery(
                Date: _date,
                Search: null,
                DepartmentId: null,
                LegalEntityId: null,
                Page: 1,
                PageSize: 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Employees.Single().Alerts.Should().BeEmpty();
    }

    private Mock<ICurrentUser> CurrentUser(bool canManageOrg)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        currentUser.SetupGet(u => u.UserId).Returns(_userId);
        currentUser.Setup(u => u.HasPermission("org:manage")).Returns(canManageOrg);
        return currentUser;
    }
}
