using MediatR;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Today's Team Schedule (2026-10-29 design, section 6.2): population, dates, Planner flag and presence.</summary>
public class GetTeamScheduleQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Report = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    // 2026-11-02 12:00 UTC = 17:30 in Colombo (the manager), 04:00 in Los Angeles.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public readonly Mock<ICurrentUser> User = new();
        public readonly Mock<ICallerIdentityResolver> Identity = new();
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<ITeamTimeReadRepository> TeamTime = new();
        public readonly Mock<IMeetingRepository> Meetings = new();
        public readonly Mock<IMediator> Mediator = new();
        public readonly Dictionary<Guid, string> Zones = new() { [Manager] = "Asia/Colombo", [Report] = "Asia/Colombo" };
        public readonly Dictionary<Guid, TeamPersonRow> People = new() { [Report] = new TeamPersonRow(Report, "Priya K", null, true) };
        /// <summary>The caller's direct reports; ReportsOf adds further levels.</summary>
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
            TeamTime.Setup(x => x.GetTasksAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, TeamTaskRow> { [TaskId] = new(TaskId, "T-1", "Write report", null, null, false, 0) });
            Meetings.Setup(x => x.ListEmployeesDayMeetingsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, DateTimeOffset _, DateTimeOffset _, CancellationToken _) =>
                    ids.ToDictionary(id => id, _ => (IReadOnlyList<EmployeeDayMeeting>)[]));
        }

        public Task<Result<TeamScheduleResponse>> RunAsync(DateOnly? date = null, bool plannerEnabled = true)
        {
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(Now);
            return new GetTeamScheduleQueryHandler(User.Object, Identity.Object, Employees.Object, TeamTime.Object, Meetings.Object,
                Mediator.Object, clock.Object, Options.Create(new PlannerOptions { Enabled = plannerEnabled }))
                .Handle(new GetTeamScheduleQuery(date), default);
        }
    }

    [Fact]
    public async Task No_reports_returns_empty()
    {
        var f = new Fixture { Descendants = [] };

        var result = await f.RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Members);
        Assert.Equal(0, result.Value.Summary.People);
        Assert.Equal(new DateOnly(2026, 11, 2), result.Value.Date);
    }

    [Fact]
    public async Task Planner_off_keeps_clocked_minutes()
    {
        var f = new Fixture { Sessions = [new TeamSessionRow(Report, TaskId, Now.AddHours(-2), Now.AddHours(-1))] };

        var result = await f.RunAsync(plannerEnabled: false);

        Assert.False(result.Value!.PlannerEnabled);
        var member = Assert.Single(result.Value.Members);
        Assert.Equal(60, member.ClockedMinutes);
        Assert.Equal("planner_off", member.NotRankedReason);
        Assert.Equal(0, result.Value.Summary.UnscheduledMinutes);
        f.TeamTime.Verify(x => x.ListBlocksForEmployeesAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Future_date_rejected()
    {
        var result = await new Fixture().RunAsync(new DateOnly(2026, 11, 3));

        Assert.Equal((400, PlannerErrorCodes.NotToday), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task Yesterday_is_allowed_and_has_no_presence()
    {
        var f = new Fixture();
        f.User.Setup(x => x.HasPermission("attendance:read")).Returns(true);

        var result = await f.RunAsync(new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 11, 1), result.Value!.Date);
        Assert.Null(Assert.Single(result.Value.Members).PresenceStatus);
        f.Mediator.Verify(x => x.Send(It.IsAny<GetCoveredTeamTodayQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Non_report_never_appears()
    {
        var outsider = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        var f = new Fixture { Descendants = [Report, inactive, Manager] };
        f.People[inactive] = new TeamPersonRow(inactive, "Left Co", null, IsActive: false);
        f.People[outsider] = new TeamPersonRow(outsider, "Other Team", null, true);
        f.Sessions = [new TeamSessionRow(outsider, TaskId, Now.AddHours(-2), Now.AddHours(-1))];

        var result = await f.RunAsync();

        Assert.Equal(new[] { Report }, result.Value!.Members.Select(m => m.EmployeeId));
        Assert.Equal(0, result.Value.Members[0].ClockedMinutes);
    }

    [Fact]
    public async Task Employee_in_other_timezone_uses_own_local_day()
    {
        // 12:00 UTC on 2 Nov is 04:00 on 2 Nov in Los Angeles. A session from 1 Nov 23:00 to 2 Nov 01:00 Los Angeles time
        // counts 60 minutes on their own 2 Nov, not the 120 a Colombo or UTC day would give.
        var la = TimeSpan.FromHours(-8);
        var f = new Fixture
        {
            Sessions = [new TeamSessionRow(Report, TaskId, new DateTimeOffset(2026, 11, 1, 23, 0, 0, la), new DateTimeOffset(2026, 11, 2, 1, 0, 0, la))],
        };
        f.Zones[Report] = "America/Los_Angeles";

        var result = await f.RunAsync();

        var member = Assert.Single(result.Value!.Members);
        Assert.Equal(("America/Los_Angeles", 60), (member.Timezone, member.ClockedMinutes));
    }

    [Fact]
    public async Task Presence_comes_from_team_status_for_covered_reports_today()
    {
        var f = new Fixture();
        f.User.Setup(x => x.HasPermission("attendance:read")).Returns(true);
        f.Mediator.Setup(x => x.Send(It.IsAny<GetCoveredTeamTodayQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamTodayResponse>.Success(new TeamTodayResponse(new DateOnly(2026, 11, 2), "Asia/Colombo", Guid.NewGuid(),
                new TeamTodaySummary(1, 1, 0, 0, 0, 0, 0, 0, 0, 0),
                [new TeamTodayMember(Report, "Priya K", null, null, "working", "Working", false, null, null, null, null, null, null)], 1, Now)));

        var result = await f.RunAsync();

        Assert.Equal("working", Assert.Single(result.Value!.Members).PresenceStatus);
    }

    [Fact]
    public async Task Caller_without_an_employee_gets_404()
    {
        var f = new Fixture();
        f.Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await f.RunAsync();

        Assert.Equal(404, result.StatusCode);
    }

    // ---- Coverage audit (2026-10-29 plan, Task 12) ----

    [Fact]
    public async Task Every_read_is_for_the_callers_tenant_only()
    {
        var f = new Fixture();

        await f.RunAsync();

        f.TeamTime.Verify(x => x.ListSessionsForEmployeesAsync(It.Is<Guid>(t => t != TenantId), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        f.TeamTime.Verify(x => x.ListBlocksForEmployeesAsync(It.Is<Guid>(t => t != TenantId), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Meetings.Verify(x => x.ListEmployeesDayMeetingsAsync(It.Is<Guid>(t => t != TenantId), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Employees.Verify(x => x.ListDirectReportEmployeeIdsAsync(It.Is<Guid>(t => t != TenantId), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_caller_never_appears_in_their_own_team()
    {
        var f = new Fixture { Descendants = [Manager, Report] };
        f.People[Manager] = new TeamPersonRow(Manager, "Me", null, true);

        var result = await f.RunAsync();

        Assert.DoesNotContain(result.Value!.Members, m => m.EmployeeId == Manager);
    }

    [Fact]
    public async Task Today_is_allowed_by_date_too()
    {
        var result = await new Fixture().RunAsync(new DateOnly(2026, 11, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 11, 2), result.Value!.Date);
    }

    [Fact]
    public async Task Reports_of_reports_are_included_and_a_cycle_does_not_loop()
    {
        var skip = Guid.NewGuid();
        var f = new Fixture();
        f.People[skip] = new TeamPersonRow(skip, "Skip Level", null, true);
        f.ReportsOf[Report] = [skip];
        f.ReportsOf[skip] = [Manager, Report];   // bad data: a cycle back up the line

        var result = await f.RunAsync();

        Assert.Equal(new[] { Report, skip }.Order(), result.Value!.Members.Select(m => m.EmployeeId).Order());
    }
}
