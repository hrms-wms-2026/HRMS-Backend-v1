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

public sealed class GetEmployeeAttendanceDisciplineCompareTests
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
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeAttendanceDisciplineCompareTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 21));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
    }

    private GetEmployeeAttendanceDisciplineQueryHandler CreateHandler() =>
        new(_guard.Object, _reader.Object, _toggles.Object, _notifications.Object, _user.Object, _clock.Object);

    private AttendanceRecord Late(DateOnly d) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = new DateTimeOffset(d.Year, d.Month, d.Day, 4, 30, 0, TimeSpan.Zero),   // 10:00 Colombo = late
        ActualEnd = new DateTimeOffset(d.Year, d.Month, d.Day, 12, 0, 0, TimeSpan.Zero)
    };

    private void ArrangePeriod(DateOnly from, DateOnly to, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.Is<EmployeePeriod>(p => p.From == from && p.To == to), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-09-21T00:00:00+00:00"), new DateOnly(2026, 9, 21), 60,
                new Dictionary<DateOnly, int>(), Array.Empty<LeaveRequest>(), DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
                Weekdays(from, to, new DateOnly(2026, 9, 21))));

    [Fact]
    public async Task Handle_WithoutCompare_HasNoPrevious_AndLoadsOnlyTheCurrentPeriod()
    {
        ArrangePeriod(SepFrom, SepTo, Late(new DateOnly(2026, 9, 2)));

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.Value!.Previous.Should().BeNull();
        _reader.Verify(r => r.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_MeasuresThePreviousMonthWithTheSameRules()
    {
        ArrangePeriod(SepFrom, SepTo, Late(new DateOnly(2026, 9, 2)));
        ArrangePeriod(AugFrom, AugTo, Late(new DateOnly(2026, 8, 3)), Late(new DateOnly(2026, 8, 4)), Late(new DateOnly(2026, 8, 5)));

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.LateClockIns.Should().Be(1);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.LateClockIns.Should().Be(3);
        result.Value.Previous.LocationViolations.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PreviousLocationViolationsAreNull_WhenTrackingIsOff()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ArrangePeriod(SepFrom, SepTo);
        ArrangePeriod(AugFrom, AugTo);

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.LocationViolations.Should().BeNull();
        result.Value.Previous!.LocationViolations.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValue()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "last-year"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    private static ExpectedWorkdays Weekdays(DateOnly from, DateOnly to, DateOnly today) =>
        ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), from, to, new DateOnly(2020, 1, 1), null, today);
}
