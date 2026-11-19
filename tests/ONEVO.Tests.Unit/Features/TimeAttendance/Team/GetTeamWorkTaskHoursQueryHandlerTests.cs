using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

/// <summary>My Team "Work vs task hours": attendance working time against completed tasks' estimates.</summary>
public sealed class GetTeamWorkTaskHoursQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-09-30T10:00:00+00:00"); // Wed 15:30 Colombo
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly DateOnly Wednesday = new(2026, 9, 30);
    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);

    private sealed class Fixture
    {
        public readonly Mock<ICurrentUser> CurrentUser = new();
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<IEmployeeAuthorityResolver> Authority = new();
        public readonly Mock<IAttendanceReadRepository> Attendance = new();
        public readonly Mock<ITeamCompletedTaskReader> Tasks = new();
        public List<AttendanceRecord> Records = [];
        public List<TeamCompletedTask> Completed = [];
        public Dictionary<Guid, AttendanceHistoryEmployee> Identities = [];

        public Fixture(params Guid[] covered)
        {
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            CurrentUser.SetupGet(x => x.UserId).Returns(UserId);
            CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
            Employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = ActorId, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId });
            Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, covered));
            Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> _, DateOnly from, DateOnly to, int _, int _, CancellationToken _) =>
                {
                    var rows = Records.Where(r => r.Date >= from && r.Date <= to).ToList();
                    return (rows, rows.Count);
                });
            Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Identities);
            Tasks.Setup(x => x.ListCompletedAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> _, DateTimeOffset from, DateTimeOffset to, CancellationToken _) =>
                    Completed.Where(t => t.CompletedAt >= from && t.CompletedAt < to).ToList());
        }

        public GetTeamWorkTaskHoursQueryHandler Handler()
        {
            var legalEntities = new Mock<ILegalEntityRepository>();
            legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LegalEntity
                {
                    Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo", StandardWorkingDays = "[1,2,3,4,5]",
                    WorkStartTime = new(9, 0), WorkEndTime = new(17, 30), BreakDurationMinutes = 60,
                });
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(UtcNow);
            return new GetTeamWorkTaskHoursQueryHandler(CurrentUser.Object, Employees.Object, legalEntities.Object, Authority.Object,
                Attendance.Object, Tasks.Object, clock.Object);
        }
    }

    private static AttendanceRecord Record(Guid employeeId, DateOnly date, int worked, int breaks) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Date = date, WorkedMinutes = worked, BreakMinutes = breaks,
        ActualStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(3, 30)), TimeSpan.Zero),
        ActualEnd = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 30)), TimeSpan.Zero),
    };

    /// <summary>A task completed at the given Colombo local time.</summary>
    private static TeamCompletedTask Done(Guid employeeId, DateOnly date, decimal? hours, int localHour = 14) =>
        new(Guid.NewGuid(), employeeId, hours, new DateTimeOffset(date.ToDateTime(new TimeOnly(localHour, 0)), Colombo));

    private static AttendanceHistoryEmployee Identity(Guid id, string name) =>
        new(id, name, "E-" + name, null, "Engineering", null);

    [Fact]
    public async Task Week_totals_compare_working_time_with_completed_task_hours()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(member)
        {
            Records = [Record(member, Monday, 480, 60), Record(member, Tuesday, 420, 30)],
            Completed = [Done(member, Monday, 3m), Done(member, Tuesday, 2.5m), Done(member, Tuesday, null)],
        };

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", null), default)).Value!;

        Assert.Equal(("week", Monday, Monday.AddDays(6), true), (r.Period, r.RangeStart, r.RangeEnd, r.IsProvisional));
        Assert.Equal((900, 90, 330, 3, 1), (r.Totals.WorkedMinutes, r.Totals.BreakMinutes, r.Totals.TaskMinutes,
            r.Totals.CompletedTasks, r.Totals.UnestimatedTasks));
        Assert.Equal(36.7m, r.Totals.Utilization);
        Assert.Equal(7, r.Trend.Count);
        Assert.Equal((480, 180, 37.5m), (r.Trend[0].WorkedMinutes, r.Trend[0].TaskMinutes, r.Trend[0].Utilization));
        Assert.Null(r.Trend[4].Utilization);
    }

    [Fact]
    public async Task Day_period_totals_one_day_and_trends_its_week()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(member)
        {
            Records = [Record(member, Monday, 480, 60), Record(member, Tuesday, 400, 20)],
            Completed = [Done(member, Monday, 4m), Done(member, Tuesday, 1m)],
        };

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("day", Tuesday), default)).Value!;

        Assert.Equal((Tuesday, Tuesday, false), (r.RangeStart, r.RangeEnd, r.IsProvisional));
        Assert.Equal((400, 60), (r.Totals.WorkedMinutes, r.Totals.TaskMinutes));
        Assert.Equal((480, 240), (r.Previous.WorkedMinutes, r.Previous.TaskMinutes));
        Assert.Equal((Monday, Monday.AddDays(6)), (r.TrendStart, r.TrendEnd));
    }

    [Fact]
    public async Task Month_previous_is_the_whole_previous_month()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(member)
        {
            Records = [Record(member, new DateOnly(2026, 8, 3), 300, 0), Record(member, new DateOnly(2026, 8, 31), 200, 0)],
        };

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("month", null), default)).Value!;

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (r.RangeStart, r.RangeEnd));
        Assert.Equal(30, r.Trend.Count);
        Assert.Equal(500, r.Previous.WorkedMinutes);
    }

    [Fact]
    public async Task Task_is_dated_by_local_completion_day()
    {
        var member = Guid.NewGuid();
        // 01:00 Tuesday in Colombo is still Monday in UTC: it belongs to Tuesday.
        var f = new Fixture(member) { Completed = [Done(member, Tuesday, 2m, localHour: 1)] };

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", Monday), default)).Value!;

        Assert.Equal(0, r.Trend[0].TaskMinutes);
        Assert.Equal(120, r.Trend[1].TaskMinutes);
    }

    [Fact]
    public async Task Employees_are_ranked_by_utilization_and_unworked_last()
    {
        var high = Guid.NewGuid();
        var low = Guid.NewGuid();
        var idle = Guid.NewGuid();
        var f = new Fixture(low, idle, high)
        {
            Records = [Record(high, Monday, 240, 0), Record(low, Monday, 480, 0)],
            Completed = [Done(high, Monday, 4m), Done(low, Monday, 2m)],
            Identities = new() { [high] = Identity(high, "Hana"), [low] = Identity(low, "Lal"), [idle] = Identity(idle, "Ida") },
        };

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", Monday), default)).Value!;

        Assert.Equal(["Hana", "Lal", "Ida"], r.Employees.Select(e => e.Name));
        Assert.Equal([1, 2, (int?)null], r.Employees.Select(e => e.Rank));
        Assert.Equal(100m, r.Employees[0].Utilization);
        Assert.Equal("Engineering", r.Employees[0].Department);
        Assert.Equal(3, r.TeamSize);
    }

    [Fact]
    public async Task The_caller_is_never_in_their_own_team()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(ActorId, member);

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", null), default)).Value!;

        Assert.Equal(1, r.TeamSize);
        f.Tasks.Verify(x => x.ListCompletedAsync(TenantId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(member)),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Future_anchor_is_clamped_to_today()
    {
        var r = (await new Fixture(Guid.NewGuid()).Handler()
            .Handle(new GetTeamWorkTaskHoursQuery("day", Wednesday.AddDays(5)), default)).Value!;

        Assert.Equal((Wednesday, true), (r.RangeStart, r.IsProvisional));
    }

    [Fact]
    public async Task Anchor_older_than_two_years_is_rejected()
    {
        var result = await new Fixture().Handler().Handle(new GetTeamWorkTaskHoursQuery("day", Wednesday.AddYears(-3)), default);

        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Forbidden_without_attendance_read()
    {
        var f = new Fixture();
        f.CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(false);

        var result = await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", null), default);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Empty_team_reads_nothing()
    {
        var f = new Fixture();

        var r = (await f.Handler().Handle(new GetTeamWorkTaskHoursQuery("week", null), default)).Value!;

        Assert.Equal(0, r.TeamSize);
        Assert.Empty(r.Employees);
        f.Tasks.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("day", true)]
    [InlineData("week", true)]
    [InlineData("month", true)]
    [InlineData("year", false)]
    [InlineData("", false)]
    public void Period_must_be_day_week_or_month(string period, bool valid)
    {
        Assert.Equal(valid, new GetTeamWorkTaskHoursQueryValidator().Validate(new GetTeamWorkTaskHoursQuery(period, null)).IsValid);
    }
}

public sealed class TeamWorkTaskMathTests
{
    [Fact]
    public void Ranges_and_previous_periods()
    {
        var thursday = new DateOnly(2026, 10, 1);
        Assert.Equal((thursday, thursday), TeamWorkTaskMath.RangeOf("day", thursday));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4)), TeamWorkTaskMath.RangeOf("week", thursday));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), TeamWorkTaskMath.RangeOf("month", thursday));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), TeamWorkTaskMath.PreviousOf("month", new DateOnly(2026, 3, 1)));
        Assert.Equal((new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27)), TeamWorkTaskMath.PreviousOf("week", new DateOnly(2026, 9, 28)));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(60, 480, 12.5)]
    [InlineData(600, 480, 125.0)]
    [InlineData(1, 3, 33.3)]
    public void Utilization(int task, int worked, double? expected)
    {
        Assert.Equal(expected is null ? null : (decimal)expected.Value, TeamWorkTaskMath.Utilization(task, worked));
    }

    [Fact]
    public void Estimates_and_ranks()
    {
        Assert.Equal(90, TeamWorkTaskMath.EstimateMinutes(1.5m));
        Assert.Equal(0, TeamWorkTaskMath.EstimateMinutes(null));
        Assert.Equal(0, TeamWorkTaskMath.EstimateMinutes(-2m));
        decimal[] all = [80m, 50m, 50m, 10m];
        Assert.Equal([1, 2, 2, 4], all.Select(v => TeamWorkTaskMath.RankOf(v, all)));
        Assert.Null(TeamWorkTaskMath.RankOf(null, all));
    }
}
