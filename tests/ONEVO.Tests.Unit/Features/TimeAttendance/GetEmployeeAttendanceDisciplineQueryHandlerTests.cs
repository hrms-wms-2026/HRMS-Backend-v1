using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class GetEmployeeAttendanceDisciplineQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset RangeStart = DateTimeOffset.Parse("2026-07-31T18:30:00+00:00");
    private static readonly DateTimeOffset RangeEnd = DateTimeOffset.Parse("2026-08-31T18:30:00+00:00");

    public GetEmployeeAttendanceDisciplineQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, RangeStart, RangeEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    private GetEmployeeAttendanceDisciplineQueryHandler CreateHandler() =>
        new(_guard.Object, _reader.Object, _toggles.Object, _notifications.Object, _user.Object, _clock.Object);

    private AttendanceRecord Rec(DateOnly d, string? start = null, string? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeData(int? allowance, Dictionary<DateOnly, int> breaks, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks,
                Array.Empty<LeaveRequest>(), RangeStart, RangeEnd,
                Weekdays(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), Today)));

    private static readonly DateOnly D3 = new(2026, 8, 3);
    private static readonly DateOnly D4 = new(2026, 8, 4);
    private static readonly DateOnly D5 = new(2026, 8, 5);
    private static readonly DateOnly D6 = new(2026, 8, 6);

    private AttendanceRecord[] Standard() => new[]
    {
        Rec(D3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
        Rec(D4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),  // late
        Rec(D5, "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00"),  // early out
        Rec(D6, "2026-08-06T03:30:00+00:00")                                // missing clock-out
    };

    [Fact]
    public async Task Handle_DoesNotRequireAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        ArrangeData(null, new Dictionary<DateOnly, int>());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CountsLateEarlyMissingAndOverBreak_AndLocationAlertsWhenTrackingIsOn()
    {
        ArrangeData(60, new Dictionary<DateOnly, int> { [D3] = 30, [D4] = 75, [D5] = 90, [D6] = 60 }, Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        var v = result.Value!;
        v.LateClockIns.Should().Be(1);
        v.EarlyClockOuts.Should().Be(1);
        v.MissingClockOuts.Should().Be(1);
        v.OverBreakDays.Should().Be(2);          // D4 (+15) and D5 (+30); D6 == allowance is not over
        v.OverBreakMinutes.Should().Be(45);
        v.LocationTrackingEnabled.Should().BeTrue();
        v.LocationViolations.Should().Be(3);
    }

    [Fact]
    public async Task Handle_HidesLocationViolations_WhenLocationTrackingIsDisabled()
    {
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ArrangeData(60, new Dictionary<DateOnly, int>(), Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.LocationTrackingEnabled.Should().BeFalse();
        result.Value.LocationViolations.Should().BeNull();
        _notifications.Verify(n => n.CountByTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CountsNoOverBreak_WhenNoAllowanceIsConfigured()
    {
        ArrangeData(null, new Dictionary<DateOnly, int> { [D4] = 500 }, Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.OverBreakDays.Should().Be(0);
        result.Value.OverBreakMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    private static ExpectedWorkdays Weekdays(DateOnly from, DateOnly to, DateOnly today) =>
        ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), from, to, new DateOnly(2020, 1, 1), null, today);
}
