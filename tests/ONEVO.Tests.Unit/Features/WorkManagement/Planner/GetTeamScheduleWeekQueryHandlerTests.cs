using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Team week planned vs actual (2026-11-02 design match, section 7.4): week, population, local days, previous span.</summary>
public class GetTeamScheduleWeekQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Report = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);
    // Wednesday 2026-10-28 12:00 in Colombo (the manager and Report).
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, Colombo);
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private sealed class Fixture
    {
        public readonly Mock<ICurrentUser> User = new();
        public readonly Mock<ICallerIdentityResolver> Identity = new();
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<ITeamTimeReadRepository> TeamTime = new();
        public readonly Mock<IMeetingRepository> Meetings = new();
        public readonly Dictionary<Guid, string> Zones = new() { [Manager] = "Asia/Colombo", [Report] = "Asia/Colombo" };
        public readonly Dictionary<Guid, TeamPersonRow> People = new() { [Report] = new TeamPersonRow(Report, "Priya K", null, true) };
        public List<Guid> Descendants = [Report];
        public readonly Dictionary<Guid, List<Guid>> ReportsOf = [];
        public List<TeamSessionRow> Sessions = [];
        public List<TeamBlockRow> Blocks = [];

        public Fixture()
        {
            User.SetupGet(x => x.IsAuthenticated).Returns(true);
            User.SetupGet(x => x.TenantId).Returns(TenantId);
            User.SetupGet(x => x.UserId).Returns(UserId);
            Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Manager);
            Employees.Setup(x => x.ListDirectReportEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> managers, CancellationToken _) => managers
                    .SelectMany(m => m == Manager ? Descendants : ReportsOf.GetValueOrDefault(m) ?? [])
                    .Distinct().ToList());
            TeamTime.Setup(x => x.GetPeopleAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    People.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
            TeamTime.Setup(x => x.GetTimezonesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    Zones.Where(z => ids.Contains(z.Key)).ToDictionary(z => z.Key, z => z.Value));
            TeamTime.Setup(x => x.ListSessionsForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Sessions);
            TeamTime.Setup(x => x.ListBlocksForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Blocks);
            Meetings.Setup(x => x.ListEmployeesDayMeetingsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, DateTimeOffset _, DateTimeOffset _, CancellationToken _) =>
                    ids.ToDictionary(id => id, _ => (IReadOnlyList<EmployeeDayMeeting>)[]));
        }

        public Task<Result<TeamScheduleWeekResponse>> RunAsync(DateOnly? weekStart = null, bool plannerEnabled = true)
        {
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(Now.ToUniversalTime());
            return new GetTeamScheduleWeekQueryHandler(User.Object, Identity.Object, Employees.Object, TeamTime.Object, Meetings.Object,
                    clock.Object, Options.Create(new PlannerOptions { Enabled = plannerEnabled }))
                .Handle(new GetTeamScheduleWeekQuery(weekStart), default);
        }
    }

    private static DateTimeOffset Local(DateOnly day, int hour, int minute = 0)
        => new(day.ToDateTime(new TimeOnly(hour, minute)), Colombo);

    [Fact]
    public async Task Defaults_to_callers_current_week_monday()
    {
        var result = await new Fixture().RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(Monday, result.Value!.WeekStart);
        Assert.Equal(Monday.AddDays(6), result.Value.WeekEnd);
        Assert.Equal(7, result.Value.Days.Count);
    }

    [Fact]
    public async Task Future_week_is_400_not_today()
    {
        var result = await new Fixture().RunAsync(Monday.AddDays(7));

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(PlannerErrorCodes.NotToday, result.ErrorCode);
    }

    [Fact]
    public async Task Past_week_is_allowed()
    {
        var result = await new Fixture().RunAsync(Monday.AddDays(-7));

        Assert.True(result.IsSuccess);
        Assert.Equal(Monday.AddDays(-7), result.Value!.WeekStart);
    }

    [Fact]
    public async Task Caller_without_an_employee_gets_404()
    {
        var f = new Fixture();
        f.Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        Assert.Equal(404, (await f.RunAsync()).StatusCode);
    }

    [Fact]
    public async Task Population_is_transitive_active_reports()
    {
        var f = new Fixture();
        var grandchild = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        f.ReportsOf[Report] = [grandchild, inactive, Manager];   // Manager again = a cycle in bad data
        f.People[grandchild] = new TeamPersonRow(grandchild, "Arjun", null, true);
        f.People[inactive] = new TeamPersonRow(inactive, "Gone", null, false);
        f.People[stranger] = new TeamPersonRow(stranger, "Stranger", null, true);

        var result = await f.RunAsync();

        Assert.Equal(new[] { "Arjun", "Priya K" }, result.Value!.Members.Select(m => m.DisplayName).Order());
    }

    [Fact]
    public async Task No_reports_returns_seven_empty_days()
    {
        var result = await new Fixture { Descendants = [] }.RunAsync();

        Assert.Empty(result.Value!.Members);
        Assert.Equal(7, result.Value.Days.Count);
        Assert.All(result.Value.Days, d => Assert.Equal(0, d.ClockedMinutes));
    }

    [Fact]
    public async Task Reads_each_source_once()
    {
        var f = new Fixture();

        await f.RunAsync();

        f.TeamTime.Verify(x => x.ListBlocksForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        f.TeamTime.Verify(x => x.ListSessionsForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Meetings.Verify(x => x.ListEmployeesDayMeetingsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Each_person_split_into_their_local_days()
    {
        var f = new Fixture();
        var newYork = Guid.NewGuid();
        f.Descendants = [Report, newYork];
        f.Zones[newYork] = "America/New_York";
        f.People[newYork] = new TeamPersonRow(newYork, "Nadia", null, true);
        // 03:00-04:00 UTC on 2026-10-27 = Tuesday 08:30 in Colombo, Monday 23:00 in New York (EDT).
        var start = new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero);
        f.Sessions = [new TeamSessionRow(Report, TaskId, start, start.AddHours(1)), new TeamSessionRow(newYork, TaskId, start, start.AddHours(1))];

        var result = await f.RunAsync();

        Assert.Equal(60, result.Value!.Days[0].ClockedMinutes);   // New York's Monday
        Assert.Equal(60, result.Value.Days[1].ClockedMinutes);    // Colombo's Tuesday
        Assert.Equal(120, result.Value.Totals.ClockedMinutes);
    }

    [Fact]
    public async Task Future_days_show_planned_only()
    {
        var f = new Fixture();
        var friday = Monday.AddDays(4);
        f.Blocks = [new TeamBlockRow(Report, TaskId, Local(friday, 9), Local(friday, 11))];

        var result = await f.RunAsync();

        var day = result.Value!.Days[4];
        Assert.Equal((120, 0), (day.PlannedTaskMinutes, day.ClockedMinutes));
        Assert.Null(result.Value.Totals.CoveragePercent);   // nothing planned has elapsed yet
    }

    [Fact]
    public async Task Previous_span_matches_elapsed_part_of_this_week()
    {
        var f = new Fixture();
        var lastWednesday = Monday.AddDays(-5);
        var lastThursday = Monday.AddDays(-4);
        f.Blocks =
        [
            new TeamBlockRow(Report, TaskId, Local(lastWednesday, 9), Local(lastWednesday, 11)),    // inside the span (to Wed 12:00)
            new TeamBlockRow(Report, TaskId, Local(lastThursday, 9), Local(lastThursday, 10)),      // after it
            new TeamBlockRow(Report, TaskId, Local(Monday, 9), Local(Monday, 11)),                  // this week, elapsed
        ];
        f.Sessions =
        [
            new TeamSessionRow(Report, TaskId, Local(lastWednesday, 10), Local(lastWednesday, 11)), // in plan, inside the span
            new TeamSessionRow(Report, TaskId, Local(lastWednesday, 14), Local(lastWednesday, 15)), // after the span
            new TeamSessionRow(Report, TaskId, Local(Monday, 9), Local(Monday, 10)),
        ];

        var result = await f.RunAsync();

        Assert.Equal(new TeamWeekTotals(120, 60, 60, 0, 50.0m), result.Value!.Previous);
        Assert.Equal(50.0m, result.Value.Totals.CoveragePercent);
        Assert.Equal(60, result.Value.Totals.ClockedMinutes);
    }

    [Fact]
    public async Task Planner_off_keeps_clocked_minutes()
    {
        var f = new Fixture();
        f.Sessions = [new TeamSessionRow(Report, TaskId, Local(Monday, 9), Local(Monday, 10, 30))];

        var result = await f.RunAsync(plannerEnabled: false);

        Assert.False(result.Value!.PlannerEnabled);
        Assert.Equal(new TeamWeekDay(Monday, 0, 0, 90, 0, 0, 0), result.Value.Days[0]);
        Assert.Null(result.Value.Totals.CoveragePercent);
        f.TeamTime.Verify(x => x.ListBlocksForEmployeesAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("2026-10-26", true)]
    [InlineData("2026-10-27", false)]
    [InlineData("2026-11-01", false)]
    [InlineData(null, true)]
    public void WeekStart_must_be_monday(string? weekStart, bool valid)
        => Assert.Equal(valid, new GetTeamScheduleWeekQueryValidator()
            .Validate(new GetTeamScheduleWeekQuery(weekStart is null ? null : DateOnly.Parse(weekStart))).IsValid);
}
