using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Queries.GetMyWeeklyWorkProgress;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.WeeklyProgress;

/// <summary>Weekly Work Progress (2026-10-28 design, section 8): the caller's own week, sources read once for both weeks.</summary>
public class GetMyWeeklyWorkProgressQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    /// <summary>Wednesday 2026-10-28 08:00 UTC (zone resolves to UTC in these tests).</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeStart = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEnd = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICalendarTimezoneResolver> _timezones = new();
    private readonly Mock<IPlannerRepository> _planner = new();
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<ICalendarEventRepository> _calendar = new();
    private readonly Mock<ILeaveRequestReadRepository> _leaves = new();
    private readonly Mock<IWeeklyWorkProgressReadRepository> _weekly = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly List<LeaveRequest> _approvedLeaves = [];
    private readonly List<WeekTaskWeight> _taskWeights = [];

    private GetMyWeeklyWorkProgressQueryHandler Handler(bool plannerEnabled = false, bool hasEmployee = true)
    {
        _user.SetupGet(u => u.TenantId).Returns(TenantId);
        _user.SetupGet(u => u.UserId).Returns(UserId);
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasEmployee ? EmployeeId : null);
        _employees.Setup(e => e.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(new Employee
        {
            Id = EmployeeId, TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, HireDate = new DateOnly(2020, 1, 1),
        });
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(new LegalEntity
        {
            Id = LegalEntityId, TenantId = TenantId, Timezone = "UTC", StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(18, 0),
        });
        _timezones.Setup(t => t.ResolveForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync("UTC");
        _planner.Setup(p => p.ListBlocksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlannedTaskBlock>());
        _planner.Setup(p => p.ListSessionsAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlannerSession>());
        _meetings.Setup(m => m.ListEmployeeDayMeetingsAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeDayMeeting>());
        _attendance.Setup(a => a.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AttendanceRecord>(), 0));
        _calendar.Setup(c => c.ListHolidayDatesAsync(TenantId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateOnly>());
        _leaves.Setup(l => l.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_approvedLeaves);
        _weekly.Setup(w => w.ListWeekTaskWeightsAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_taskWeights);

        return new GetMyWeeklyWorkProgressQueryHandler(_user.Object, _identity.Object, _employees.Object, _legalEntities.Object,
            _timezones.Object, _planner.Object, _meetings.Object, _attendance.Object, _calendar.Object, _leaves.Object,
            _weekly.Object, Options.Create(new PlannerOptions { Enabled = plannerEnabled }), _clock.Object);
    }

    [Fact]
    public async Task Caller_without_an_employee_record_is_not_found()
    {
        var result = await Handler(hasEmployee: false).Handle(new GetMyWeeklyWorkProgressQuery(null), default);

        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData("2026-10-27")] // a Tuesday
    [InlineData("2026-11-02")] // next week
    [InlineData("2025-10-20")] // 53 weeks back
    public async Task Week_start_must_be_a_monday_in_the_last_52_weeks(string weekStart)
    {
        var result = await Handler().Handle(new GetMyWeeklyWorkProgressQuery(DateOnly.Parse(weekStart)), default);

        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Fifty_two_weeks_back_is_allowed()
    {
        var result = await Handler().Handle(new GetMyWeeklyWorkProgressQuery(new DateOnly(2025, 10, 27)), default);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Defaults_to_the_current_week_in_the_employee_zone()
    {
        var result = await Handler().Handle(new GetMyWeeklyWorkProgressQuery(null), default);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 28), "UTC"),
            (dto.WeekStart, dto.WeekEnd, dto.Today, dto.Timezone));
        Assert.Equal(2700, dto.ThisWeek.ExpectedMinutes);
        Assert.Equal(new DateOnly(2026, 10, 19), dto.LastWeek.Days[0].Date);
        Assert.False(dto.PlannerEnabled);
    }

    [Fact]
    public async Task Planner_flag_is_reported()
    {
        var result = await Handler(plannerEnabled: true).Handle(new GetMyWeeklyWorkProgressQuery(null), default);

        Assert.True(result.Value!.PlannerEnabled);
    }

    [Fact]
    public async Task Each_source_is_read_once_for_both_weeks()
    {
        await Handler().Handle(new GetMyWeeklyWorkProgressQuery(null), default);

        _planner.Verify(p => p.ListBlocksAsync(TenantId, EmployeeId, RangeStart, RangeEnd, It.IsAny<CancellationToken>()), Times.Once);
        _planner.Verify(p => p.ListSessionsAsync(TenantId, EmployeeId, RangeStart, RangeEnd, It.IsAny<CancellationToken>()), Times.Once);
        _meetings.Verify(m => m.ListEmployeeDayMeetingsAsync(TenantId, EmployeeId, RangeStart, RangeEnd, It.IsAny<CancellationToken>()), Times.Once);
        _attendance.Verify(a => a.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), new DateOnly(2026, 10, 19),
            new DateOnly(2026, 11, 1), 0, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _calendar.Verify(c => c.ListHolidayDatesAsync(TenantId, new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _leaves.Verify(l => l.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), new DateOnly(2026, 10, 19),
            new DateOnly(2026, 11, 1), It.IsAny<CancellationToken>()), Times.Once);
        _weekly.Verify(w => w.ListWeekTaskWeightsAsync(TenantId, EmployeeId, new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1),
            new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), RangeEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Task_weights_are_summed_by_status_category()
    {
        _taskWeights.AddRange(
        [
            new WeekTaskWeight(Guid.NewGuid(), 3m, TaskStatusCategories.Done),
            new WeekTaskWeight(Guid.NewGuid(), 5m, TaskStatusCategories.Active),
            new WeekTaskWeight(Guid.NewGuid(), 2m, TaskStatusCategories.NotStarted),
            new WeekTaskWeight(Guid.NewGuid(), null, TaskStatusCategories.Active),
        ]);

        var tasks = (await Handler().Handle(new GetMyWeeklyWorkProgressQuery(null), default)).Value!.Tasks;

        Assert.Equal((3m, 5m, 2m, 1), (tasks.CompletedHours, tasks.InProgressHours, tasks.NotStartedHours, tasks.UnestimatedCount));
    }

    [Fact]
    public async Task Approved_leave_counts_its_overlap_with_the_work_window()
    {
        _approvedLeaves.Add(Leave(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 27, 18, 0, 0, TimeSpan.Zero)));
        _approvedLeaves.Add(Leave(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero)));

        var week = (await Handler().Handle(new GetMyWeeklyWorkProgressQuery(null), default)).Value!.ThisWeek;

        Assert.Equal(540, week.Days[1].LeaveMinutes);
        Assert.Equal(0, week.Days[1].ExpectedMinutes);
        Assert.Equal(240, week.Days[3].LeaveMinutes);
        Assert.Equal(2700 - 540 - 240, week.ExpectedMinutes);
    }

    [Fact]
    public async Task Partially_cancelled_leave_stops_at_its_effective_date()
    {
        var leave = Leave(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero));
        leave.PartialCancelEffectiveDate = new DateOnly(2026, 10, 28);
        _approvedLeaves.Add(leave);

        var week = (await Handler().Handle(new GetMyWeeklyWorkProgressQuery(null), default)).Value!.ThisWeek;

        Assert.Equal(new[] { 540, 540, 0, 0, 0 }, week.Days.Take(5).Select(d => d.LeaveMinutes));
    }

    private static LeaveRequest Leave(DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, StartAt = start, EndAt = end,
        Status = LeaveRequestStatuses.Approved,
    };
}
