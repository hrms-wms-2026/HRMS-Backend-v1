using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedTimeProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>GET led-progress/time handler: led scope, filters, Planner flag and the people set.</summary>
public sealed class GetLedTimeProgressQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkLeadershipService> _leadership = new();
    private readonly Mock<ITeamTimeReadRepository> _teamTime = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICalendarEventRepository> _calendar = new();
    private readonly Mock<ILeaveRequestReadRepository> _leave = new();
    // Wednesday 2026-10-21 18:00 UTC.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero) };
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Employee _caller;
    private readonly Guid _p1 = Guid.NewGuid(), _a = Guid.NewGuid(), _b = Guid.NewGuid();
    private readonly Guid _worker = Guid.NewGuid(), _task = Guid.NewGuid();
    private IReadOnlyCollection<Guid>? _sessionScope;

    public GetLedTimeProgressQueryHandlerTests()
    {
        _caller = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId };
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_caller);
        _employees.Setup(e => e.ListByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>
            {
                [_worker] = new() { Id = _worker, TenantId = _tenantId, LegalEntityId = _legalEntityId, HireDate = new DateOnly(2020, 1, 1) },
            });
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = _legalEntityId, TenantId = _tenantId, Timezone = "UTC", StandardWorkingDays = "[1,2,3,4,5]",
                WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
            });
        _calendar.Setup(c => c.ListHolidayDatesAsync(_tenantId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _leave.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var end = new DateOnly(2026, 12, 31);
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedWorkScope(
            [
                new LedHeadModule(_a, _p1, "A", false, end, new HashSet<Guid> { _a, _b }, 0,
                    [new(_a, _p1, null, "A", false, end), new(_b, _p1, _a, "B", false, end)]),
            ], []));

        var monday = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        _teamTime.Setup(t => t.ListSessionsForTasksAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, DateTimeOffset, DateTimeOffset, CancellationToken>((_, ids, _, _, _) => _sessionScope = ids)
            .ReturnsAsync([new TeamSessionRow(_worker, _task, monday.AddHours(9), monday.AddHours(11))]);
        _teamTime.Setup(t => t.ListBlocksForTasksAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TeamBlockRow(_worker, _task, monday.AddHours(9), monday.AddHours(10))]);
        _teamTime.Setup(t => t.ListAssigneesForTasksAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_worker]);
        _teamTime.Setup(t => t.GetPeopleAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TeamPersonRow> { [_worker] = new(_worker, "Ada Lovelace", null, true) });
        _teamTime.Setup(t => t.GetTimezonesAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [_worker] = "UTC" });
        _teamTime.Setup(t => t.GetTasksAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TeamTaskRow> { [_task] = new(_task, "T-1", "Write report", _a, _p1, false, 0) });
    }

    private GetLedTimeProgressQueryHandler Handler(bool plannerEnabled = true) => new(_user.Object, _clock, _employees.Object,
        _leadership.Object, _teamTime.Object, _legalEntities.Object, _calendar.Object, _leave.Object,
        Options.Create(new PlannerOptions { Enabled = plannerEnabled }));

    [Fact]
    public async Task Planned_worked_and_workload_for_the_led_scope()
    {
        var r = (await Handler().Handle(new GetLedTimeProgressQuery("week"), default)).Value!;

        Assert.Equal(("week", new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)), (r.Period, r.From, r.To));
        Assert.Equal((60, 120, 60, 60, 0), (r.PlannedMinutes, r.ClockedMinutes, r.ScheduledMinutes, r.OutsideSlotMinutes, r.NoPlanMinutes));
        var row = Assert.Single(r.Workload);
        Assert.Equal(("Ada Lovelace", 1, 60, 120, 60, 5 * 480), (row.DisplayName, row.Rank, row.PlannedMinutes, row.ClockedMinutes,
            row.UnscheduledMinutes, row.ExpectedMinutes));
        Assert.Equal(new[] { _a, _b }.Order(), _sessionScope!.Order());
    }

    [Fact]
    public async Task Leads_nothing_returns_empty()
    {
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedWorkScope([], []));

        var r = (await Handler().Handle(new GetLedTimeProgressQuery("month"), default)).Value!;

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (r.From, r.To));
        Assert.Equal((0, 0), (r.PlannedMinutes, r.ClockedMinutes));
        Assert.Null(r.CoveragePercent);
        Assert.Empty(r.Workload);
        Assert.Equal(5, r.Buckets.Count);
        _teamTime.Verify(t => t.ListSessionsForTasksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Project_filter_outside_scope_returns_empty()
    {
        var r = (await Handler().Handle(new GetLedTimeProgressQuery("week", ProjectId: Guid.NewGuid()), default)).Value!;
        var foreignModule = (await Handler().Handle(new GetLedTimeProgressQuery("week", ObjectiveId: Guid.NewGuid()), default)).Value!;

        Assert.Empty(r.Workload);
        Assert.Equal(0, r.ClockedMinutes);
        Assert.Empty(foreignModule.Workload);
        _teamTime.Verify(t => t.ListSessionsForTasksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Module_filter_narrows_to_its_subtree()
    {
        await Handler().Handle(new GetLedTimeProgressQuery("week", ObjectiveId: _b), default);

        Assert.Equal(new[] { _b }, _sessionScope);
    }

    [Fact]
    public async Task Planner_off_keeps_clocked()
    {
        var r = (await Handler(plannerEnabled: false).Handle(new GetLedTimeProgressQuery("week"), default)).Value!;

        Assert.False(r.PlannerEnabled);
        Assert.Equal((0, 120, 0), (r.PlannedMinutes, r.ClockedMinutes, r.ScheduledMinutes));
        Assert.Empty(r.Unscheduled);
        _teamTime.Verify(t => t.ListBlocksForTasksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("week", true)]
    [InlineData("MONTH", true)]
    [InlineData("all", false)]
    [InlineData(null, false)]
    public void Period_is_required_week_or_month(string? period, bool valid)
        => Assert.Equal(valid, new GetLedTimeProgressQueryValidator().Validate(new GetLedTimeProgressQuery(period)).IsValid);
}
