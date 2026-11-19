using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedUnscheduledWork;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>GET led-progress/time/unscheduled handler: kind totals, kind filter, backend paging and the Planner flag.</summary>
public sealed class GetLedUnscheduledWorkQueryHandlerTests
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

    public GetLedUnscheduledWorkQueryHandlerTests()
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
            .ReturnsAsync([
                new TeamSessionRow(_worker, _task, monday.AddHours(9), monday.AddHours(11)),
                new TeamSessionRow(_worker, _task, monday.AddDays(1).AddHours(9), monday.AddDays(1).AddHours(9.5)),
                new TeamSessionRow(_worker, _task, monday.AddDays(2).AddHours(9), monday.AddDays(2).AddHours(9.75)),
            ]);
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

    private GetLedUnscheduledWorkQueryHandler Handler(bool plannerEnabled = true) => new(_user.Object, _clock, _employees.Object,
        _leadership.Object, _teamTime.Object, _legalEntities.Object, _calendar.Object, _leave.Object,
        Options.Create(new PlannerOptions { Enabled = plannerEnabled }));

    [Fact]
    public async Task All_kinds_newest_first_with_totals_per_kind()
    {
        var r = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week"), default)).Value!;

        Assert.Equal(("week", "all", true), (r.Period, r.Kind, r.PlannerEnabled));
        Assert.Equal(3, r.TotalCount);
        Assert.Equal(new[] { 45, 30, 60 }, r.Items.Select(i => i.Minutes));
        Assert.Equal(new[] { "no_plan", "no_plan", "outside_slot" }, r.Items.Select(i => i.Kind));
        Assert.Equal(("T-1", "Write report", "Ada Lovelace"), (r.Items[0].ShortId, r.Items[0].Title, r.Items[0].DisplayName));
        Assert.Equal(new LedUnscheduledKindTotal(3, 135), r.All);
        Assert.Equal(new LedUnscheduledKindTotal(2, 75), r.NoPlan);
        Assert.Equal(new LedUnscheduledKindTotal(1, 60), r.OutsideSlot);
    }

    [Fact]
    public async Task Kind_filter_narrows_the_rows_but_not_the_totals()
    {
        var r = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week", Kind: "OUTSIDE_SLOT"), default)).Value!;

        Assert.Equal("outside_slot", r.Kind);
        var row = Assert.Single(r.Items);
        Assert.Equal(60, row.Minutes);
        Assert.Equal(1, r.TotalCount);
        Assert.Equal(3, r.All.Count);
    }

    [Fact]
    public async Task Pages_on_the_server()
    {
        var first = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week", PageNumber: 1, PageSize: 2), default)).Value!;
        var second = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week", PageNumber: 2, PageSize: 2), default)).Value!;
        var clamped = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week", PageNumber: 0, PageSize: 1000), default)).Value!;

        Assert.Equal((2, 3, 2, true, false), (first.Items.Count, first.TotalCount, first.TotalPages, first.HasNext, first.HasPrevious));
        Assert.Equal((1, false, true), (second.Items.Count, second.HasNext, second.HasPrevious));
        Assert.Equal(60, second.Items[0].Minutes);
        Assert.Equal((1, 100, 3), (clamped.PageNumber, clamped.PageSize, clamped.Items.Count));
    }

    [Fact]
    public async Task Planner_off_returns_an_empty_page()
    {
        var r = (await Handler(plannerEnabled: false).Handle(new GetLedUnscheduledWorkQuery("week"), default)).Value!;

        Assert.False(r.PlannerEnabled);
        Assert.Empty(r.Items);
        Assert.Equal(LedUnscheduledKindTotal.Zero, r.All);
    }

    [Fact]
    public async Task Filter_outside_scope_returns_an_empty_page()
    {
        var r = (await Handler().Handle(new GetLedUnscheduledWorkQuery("week", ProjectId: Guid.NewGuid()), default)).Value!;

        Assert.Empty(r.Items);
        Assert.Equal(0, r.TotalCount);
        _teamTime.Verify(t => t.ListSessionsForTasksAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unauthenticated_is_forbidden()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(false);

        var r = await Handler().Handle(new GetLedUnscheduledWorkQuery("week"), default);

        Assert.False(r.IsSuccess);
    }

    [Theory]
    [InlineData("week", "all", true)]
    [InlineData(null, null, true)]
    [InlineData("month", "no_plan", true)]
    [InlineData("all", null, false)]
    [InlineData("week", "planned", false)]
    public void Validator_accepts_known_period_and_kind(string? period, string? kind, bool valid)
    {
        var result = new GetLedUnscheduledWorkQueryValidator().Validate(new GetLedUnscheduledWorkQuery(period, kind));

        Assert.Equal(valid, result.IsValid);
    }
}
