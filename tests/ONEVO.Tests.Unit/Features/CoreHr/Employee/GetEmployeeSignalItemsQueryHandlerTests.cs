using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeSignalItemsQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
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
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromHours(5.5), "c", "c");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    public GetEmployeeSignalItemsQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _modules.Setup(m => m.IsModuleEnabledAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(true, Guid.NewGuid(), Array.Empty<Guid>()));
        ArrangeAttendance(null, new Dictionary<DateOnly, int>());
    }

    private GetEmployeeSignalItemsQueryHandler CreateHandler() =>
        new(_guard.Object, _reader.Object, _tasks.Object, _sender.Object, _modules.Object, _toggles.Object,
            _notifications.Object, _exceptions.Object, _exceptionScope.Object, _user.Object, _clock.Object);

    private Task<Result<EmployeeSignalItemsResponse>> Run(string key) =>
        CreateHandler().Handle(new GetEmployeeSignalItemsQuery(_employeeId, key, From, To), CancellationToken.None);

    private AttendanceRecord Rec(DateOnly d, string start, string? end) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeAttendance(int? allowance, Dictionary<DateOnly, int> breaks, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks,
                Array.Empty<LeaveRequest>(), DateTimeOffset.Parse("2026-07-31T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-31T18:30:00+00:00"),
                ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), From, To, new DateOnly(2020, 1, 1), null, Today)));

    private static readonly DateOnly D3 = new(2026, 8, 3);
    private static readonly DateOnly D4 = new(2026, 8, 4);
    private static readonly DateOnly D5 = new(2026, 8, 5);

    [Fact]
    public async Task UnknownKey_Returns404()
    {
        var result = await Run("not_a_signal");
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AccessDenied_PassesThroughTheGuardStatus()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Failure("nope", 403));
        (await Run("late_clock_ins")).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task LateClockIns_ListsTheLateDays_NewestFirst_WithTheEmployeeTimezone()
    {
        ArrangeAttendance(null, new(),
            Rec(D3, "2026-08-03T04:15:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"),
            Rec(D5, "2026-08-05T04:00:00+00:00", "2026-08-05T12:00:00+00:00"));

        var r = (await Run("late_clock_ins")).Value!;

        r.Key.Should().Be("late_clock_ins");
        r.Timezone.Should().Be("Asia/Colombo");
        r.Total.Should().Be(2);
        r.Items.Select(i => i.Date).Should().Equal(D5, D3);
        r.Items.Should().OnlyContain(i => i.Kind == "attendance_day" && i.Id == i.Date.ToString("yyyy-MM-dd"));
        r.Items[0].Title.Should().Be("Wed 5 Aug");
    }

    [Fact]
    public async Task OverBreak_ListsDaysWithMinutesOver()
    {
        ArrangeAttendance(60, new() { [D3] = 60, [D4] = 95 },
            Rec(D3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"));

        var r = (await Run("over_break")).Value!;

        r.Total.Should().Be(1);
        r.Items.Single().Date.Should().Be(D4);
        r.Items.Single().Subtitle.Should().Be("35 min over allowance");
    }

    [Theory]
    [InlineData("absent_days")]
    [InlineData("missing_clock_outs")]
    [InlineData("late_clock_ins")]
    [InlineData("early_clock_outs")]
    [InlineData("short_hours_days")]
    [InlineData("off_schedule_work")]
    public async Task AttendanceKeys_TotalEqualsTheClassifierCountBehindTheSignal(string key)
    {
        var records = new[]
        {
            Rec(D3, "2026-08-03T04:15:00+00:00", "2026-08-03T05:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", null),
            Rec(new DateOnly(2026, 8, 8), "2026-08-08T03:30:00+00:00", "2026-08-08T08:00:00+00:00")
        };
        ArrangeAttendance(null, new(), records);
        var data = await _reader.Object.LoadAsync(_tenantId, _employeeId, null, EmployeePeriod.Resolve(From, To, Today).Value!);
        var c = AttendancePeriodCalculator.Classify(data);
        var expected = key switch
        {
            "absent_days" => c.Absent, "missing_clock_outs" => c.MissingClockOuts, "late_clock_ins" => c.Late,
            "early_clock_outs" => c.EarlyDepartures, "short_hours_days" => c.ShortHours,
            _ => c.WorkedOnNonWorkingDay + c.WorkedDuringTimeOff
        };

        var r = (await Run(key)).Value!;

        r.Total.Should().Be(expected);
        r.Items.Should().HaveCount(Math.Min(expected, GetEmployeeSignalItemsQueryHandler.MaxItems));
    }
}
