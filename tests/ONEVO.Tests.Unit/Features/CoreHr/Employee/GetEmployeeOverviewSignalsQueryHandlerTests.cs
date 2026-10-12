using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeOverviewSignals;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeOverviewSignalsQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IModuleEntitlementService> _modules = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionScopeResolver> _exceptionScope = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    public GetEmployeeOverviewSignalsQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 30));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));

        // Every source quiet by default, every gate open.
        ArrangeAttendance(Att());
        ArrangeDiscipline(Result<EmployeeAttendanceDisciplineResponse>.Success(new(From, To, 0, 0, 0, 0, 0, false, null)));
        _sender.Setup(s => s.Send(It.IsAny<GetEmployeeWorkOverviewQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeWorkOverviewResponse>.Success(new(From, To, 0, 0, 0, 0, 0, 0, null)));
        ArrangeApprovals(Result<EmployeeApprovalActivityResponse>.Success(
            new(From, To, 0, 0, 0, 0, Array.Empty<EmployeeApprovalItem>())));
        _modules.Setup(m => m.IsModuleEnabledAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, It.IsAny<NotificationType>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(true, Guid.NewGuid(), Array.Empty<Guid>()));
        _exceptions.Setup(e => e.CountDetectedInRangeAsync(_tenantId, _employeeId,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private GetEmployeeOverviewSignalsQueryHandler CreateHandler() =>
        new(_guard.Object, _sender.Object, _modules.Object, _toggles.Object, _notifications.Object,
            _exceptions.Object, _exceptionScope.Object, _user.Object, _clock.Object,
            NullLogger<GetEmployeeOverviewSignalsQueryHandler>.Instance);

    private Task<Result<EmployeeOverviewSignalsResponse>> Run() =>
        CreateHandler().Handle(new GetEmployeeOverviewSignalsQuery(_employeeId, From, To), CancellationToken.None);

    private static EmployeeAttendanceOverviewResponse Att(int absent = 0, int late = 0) =>
        new(From, To, 22, 10, late, 0, 0, Array.Empty<EmployeeAttendanceDay>(), absent, 0, 0, 0);

    private void ArrangeAttendance(EmployeeAttendanceOverviewResponse response) =>
        _sender.Setup(s => s.Send(It.IsAny<GetEmployeeAttendanceOverviewQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeAttendanceOverviewResponse>.Success(response));

    private void ArrangeDiscipline(Result<EmployeeAttendanceDisciplineResponse> result) =>
        _sender.Setup(s => s.Send(It.IsAny<GetEmployeeAttendanceDisciplineQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void ArrangeApprovals(Result<EmployeeApprovalActivityResponse> result) =>
        _sender.Setup(s => s.Send(It.IsAny<GetEmployeeApprovalActivityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task GuardFailure_Returns403_AndSendsNothing()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden());

        var result = await Run();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _sender.Invocations.Should().BeEmpty();
        _notifications.Invocations.Should().BeEmpty();
        _exceptions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkModuleOff_OmitsOverdueTasks()
    {
        _modules.Setup(m => m.IsModuleEnabledAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ArrangeAttendance(Att(absent: 2));

        var result = await Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Signals.Select(s => s.Key).Should().Equal("absent_days");
        _sender.Verify(s => s.Send(It.IsAny<GetEmployeeWorkOverviewQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MonitoringOff_OmitsMonitoringSignals()
    {
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Signals.Should().NotContain(s => s.Category == "monitoring");
        _notifications.Invocations.Should().BeEmpty();
        _exceptions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FailingSource_IsIsolated()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetEmployeeAttendanceDisciplineQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        ArrangeAttendance(Att(late: 1));

        var result = await Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Signals.Select(s => s.Key).Should().Contain("late_clock_ins");
    }

    [Fact]
    public async Task FailedResult_TreatedAsMissing()
    {
        ArrangeApprovals(Result<EmployeeApprovalActivityResponse>.Forbidden());

        var result = await Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Signals.Should().NotContain(s => s.Key == "pending_approvals");
    }

    [Fact]
    public async Task ExceptionsOutsideViewerScope_OmitsExceptionCount_KeepsIdleAlerts()
    {
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.LongIdleAlert,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);

        var result = await Run();

        result.Value!.Signals.Select(s => s.Key).Should().Equal("idle_activity_alerts");
        _exceptions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExceptionsInsideViewerScope_AreCounted()
    {
        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Guid.NewGuid(), new[] { _employeeId }));
        _exceptions.Setup(e => e.CountDetectedInRangeAsync(_tenantId, _employeeId,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await Run();

        var signal = result.Value!.Signals.Single();
        (signal.Key, signal.Value).Should().Be(("monitoring_exceptions", 3));
    }
}
