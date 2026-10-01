using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class GetEmployeeAttendanceOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);

    public GetEmployeeAttendanceOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, _legalEntityId, null, "full_time", "active", null, null)));
    }

    private GetEmployeeAttendanceOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _reader.Object, _user.Object, _clock.Object);

    private AttendanceRecord Rec(DateOnly d, string? start = null, string? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeData(IReadOnlyList<AttendanceRecord> records, IReadOnlyList<LeaveRequest>? leaves = null) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, 60,
                new Dictionary<DateOnly, int>(), leaves ?? Array.Empty<LeaveRequest>(),
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

    [Fact]
    public async Task Handle_PassesThroughGuardFailure_WithoutReadingData()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _reader.Verify(r => r.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksAttendanceReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AllowsSelfWithoutAttendanceRead()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        ArrangeData(Array.Empty<AttendanceRecord>());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DefaultsToTheCurrentMonth_WhenNoRangeIsGiven()
    {
        ArrangeData(Array.Empty<AttendanceRecord>());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.From.Should().Be(new DateOnly(2026, 8, 1));
        result.Value.To.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public async Task Handle_CountsSummaryAndBuildsTheDayStrip()
    {
        var leave = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = _employeeId,
            StartAt = DateTimeOffset.Parse("2026-08-07T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-08-07T23:59:59+00:00")
        };
        ArrangeData(
            new[]
            {
                Rec(new(2026, 8, 3), "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),  // present
                Rec(new(2026, 8, 4), "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),  // late
                Rec(new(2026, 8, 6), "2026-08-06T03:30:00+00:00"),                               // missing clock-out
                Rec(new(2026, 8, 7)),                                                            // leave
                Rec(new(2026, 8, 10))                                                            // absent
            },
            new[] { leave });

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        var v = result.Value!;
        v.WorkingDays.Should().Be(15); // Mon–Fri from Aug 3 up to today (Aug 21)
        v.Present.Should().Be(3);
        v.Late.Should().Be(1);
        v.MissingClockOuts.Should().Be(1);
        v.LeaveDays.Should().Be(1);
        v.Days.Should().HaveCount(31);
        v.Days.Select(d => d.Date).Should().BeInAscendingOrder();
        v.Days.Take(10).Select(d => d.Status).Should().Equal(
            "off", "off",                                                 // Sat 1, Sun 2
            "present", "late", "absent", "missing_clock_out", "leave",    // Mon 3 – Fri 7 (5th has no row)
            "off", "off",                                                 // Sat 8, Sun 9
            "absent");                                                    // Mon 10
    }

    [Fact]
    public async Task Handle_FillsEveryCalendarDay_WhenOnlyOneRecordExists()
    {
        ArrangeData(new[] { Rec(new(2026, 8, 4), "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00") });

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        var v = result.Value!;
        v.WorkingDays.Should().Be(15);
        v.Present.Should().Be(1);
        v.Days.Should().HaveCount(31);
        v.Days.Single(d => d.Date == new DateOnly(2026, 8, 4)).Status.Should().Be("late");
        v.Days.Single(d => d.Date == new DateOnly(2026, 8, 3)).Status.Should().Be("absent");
        v.Days.Single(d => d.Date == new DateOnly(2026, 8, 21)).Status.Should().Be("none"); // today, not clocked in yet
        v.Days.Single(d => d.Date == new DateOnly(2026, 8, 24)).Status.Should().Be("none"); // future
        v.Days.Single(d => d.Date == new DateOnly(2026, 8, 29)).Status.Should().Be("off");  // Saturday
    }

    [Fact]
    public async Task Handle_UsesTheLegalEntityWorkingWeek_ForDaysWithoutRecords()
    {
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                Array.Empty<AttendanceRecord>(), Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, 60,
                new Dictionary<DateOnly, int>(), Array.Empty<LeaveRequest>(),
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
                new HashSet<int> { 1, 2, 3, 4, 5, 6 })); // Mon–Sat

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        result.Value!.Days.Single(d => d.Date == new DateOnly(2026, 8, 1)).Status.Should().Be("absent"); // Saturday
        result.Value.Days.Single(d => d.Date == new DateOnly(2026, 8, 2)).Status.Should().Be("off");     // Sunday
    }
}
