using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class TeamAttendanceQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid DeptA = Guid.NewGuid(), DeptB = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 28); // Wednesday

    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ITeamAttendancePeriodReader> _reader = new();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly Employee _actor = new() { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId };
    private readonly List<Guid> _covered = [];
    private readonly Dictionary<Guid, AttendanceHistoryEmployee> _identities = [];
    private readonly List<TeamAttendanceEmployeeDays> _days = [];
    private readonly List<TeamTodayMember> _members = [];
    private TeamTodaySummary _summary = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public TeamAttendanceQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(TenantId);
        _user.SetupGet(u => u.UserId).Returns(UserId);
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-10-28T10:00:00+00:00"));
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_actor);
        _authority.Setup(a => a.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, _covered.Append(_actor.Id).ToList()));
        _attendance.Setup(a => a.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _identities);
        _reader.Setup(r => r.LoadAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _days);
        _mediator.Setup(m => m.Send(It.IsAny<GetCoveredTeamTodayQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<TeamTodayResponse>.Success(new TeamTodayResponse(
                Today, "UTC", LegalEntityId, _summary, _members.ToList(), _members.Count)));
    }

    private TeamAttendanceQueryHandler Handler() => new(
        _user.Object, _employees.Object, _authority.Object, _mediator.Object, _reader.Object, _attendance.Object, _clock.Object);

    /// <summary>A covered employee with a bucket per date, given as (date, bucket) pairs; other dates are off.</summary>
    private Guid Member(string name, Guid? department, params (string Date, string Bucket)[] days)
    {
        var id = Guid.NewGuid();
        _covered.Add(id);
        _identities[id] = new AttendanceHistoryEmployee(id, name, "E" + name, null, department == DeptA ? "Alpha" : department == DeptB ? "Beta" : null, null);
        var map = new Dictionary<DateOnly, string>();
        for (var d = new DateOnly(2026, 9, 1); d <= new DateOnly(2026, 11, 30); d = d.AddDays(1)) map[d] = TeamAttendanceBuckets.Off;
        foreach (var (date, bucket) in days) map[DateOnly.Parse(date)] = bucket;
        _days.Add(new TeamAttendanceEmployeeDays(id, department, map));
        return id;
    }

    private void TodayStatus(Guid id, string status, bool isLate = false, DateTimeOffset? clockIn = null) =>
        _members.Add(new TeamTodayMember(id, _identities[id].DisplayName, null, null, status, status, isLate, clockIn, null, null, null, null, null));

    private static GetTeamAttendanceSummaryQuery Week(DateOnly? anchor = null, Guid? department = null) => new("week", anchor, department);

    [Fact]
    public async Task Summary_NoPermission_Forbidden()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        var result = await Handler().Handle(Week(), CancellationToken.None);
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Summary_BadMode_Failure()
    {
        var result = await Handler().Handle(new GetTeamAttendanceSummaryQuery("year", null, null), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Summary_RateExcludesLeaveAndPending()
    {
        // Mon 26 + Tue 27: 4 attended, 1 leave; no absent → 100%.
        Member("Ann", null, ("2026-10-26", "present"), ("2026-10-27", "late"));
        Member("Bob", null, ("2026-10-26", "present"), ("2026-10-27", "leave"));
        Member("Cat", null, ("2026-10-26", "present"));
        var a = _covered[0];
        TodayStatus(a, "not_started");

        var r = (await Handler().Handle(Week(), CancellationToken.None)).Value!;

        r.PeriodCounts.Should().Be(new TeamAttendanceCounts(3, 1, 0, 1, 1));
        r.PeriodRate.Should().Be(100m);
        r.RangeStart.Should().Be(new DateOnly(2026, 10, 26));
        r.RangeEnd.Should().Be(new DateOnly(2026, 11, 1));
        r.IsProvisional.Should().BeTrue();
        r.Trend.Should().HaveCount(7);
        r.Trend.Single(p => p.Date == new DateOnly(2026, 10, 30)).Rate.Should().BeNull();
    }

    [Fact]
    public async Task Summary_DeltaPp_IsPercentagePoints()
    {
        // Previous week clipped to Mon 19..Wed 21: 9 attended of 10 = 90%. Current Mon 26..Wed 28: 24 of 25 = 96%.
        for (var i = 0; i < 5; i++)
        {
            var prev = new List<(string, string)> { ("2026-10-19", "present"), ("2026-10-20", i == 0 ? "absent" : "present"), ("2026-10-22", "absent") };
            var cur = new List<(string, string)> { ("2026-10-26", "present"), ("2026-10-27", "present") };
            Member("P" + i, null, [.. prev, .. cur]);
        }
        for (var i = 0; i < 15; i++) Member("Q" + i, null, ("2026-10-28", "present"));
        foreach (var id in _covered.Skip(5).Take(14)) TodayStatus(id, "working");
        TodayStatus(_covered[19], "absent");

        var r = (await Handler().Handle(Week(), CancellationToken.None)).Value!;

        r.PreviousRate.Should().Be(90m);
        r.PeriodRate.Should().Be(96m);
        r.DeltaPp.Should().Be(6m);
    }

    [Fact]
    public async Task Summary_TodayUsesTeamTodayStatuses()
    {
        var a = Member("Ann", null, ("2026-10-28", "absent"));
        var b = Member("Bob", null, ("2026-10-28", "off"));
        TodayStatus(a, "not_started");
        TodayStatus(b, "clocked_out", isLate: true);
        _summary = new TeamTodaySummary(2, 0, 0, 1, 1, 0, 1, 0, 0, 0);

        var r = (await Handler().Handle(Week(), CancellationToken.None)).Value!;

        var today = r.Trend.Single(p => p.Date == Today);
        today.Pending.Should().Be(1);
        today.Late.Should().Be(1);
        today.Absent.Should().Be(0);
        r.TodayCounts.Should().Be(new TeamAttendanceCounts(0, 1, 0, 0, 1));
        r.EligibleToday.Should().Be(2);
        r.TodayRate.Should().Be(100m);
    }

    [Fact]
    public async Task Summary_TodayCountsUseSummaryNotCappedMembers()
    {
        Member("Ann", null);
        TodayStatus(_covered[0], "working");
        // 250 people team-wide; Members was capped.
        _summary = new TeamTodaySummary(250, 180, 10, 20, 15, 5, 20, 10, 5, 0);

        var r = (await Handler().Handle(Week(), CancellationToken.None)).Value!;

        r.TodayCounts.Should().Be(new TeamAttendanceCounts(195, 15, 5, 10, 20));
        r.EligibleToday.Should().Be(245);
    }

    [Fact]
    public async Task Summary_DepartmentFilter_UsesFilteredBuckets()
    {
        var a = Member("Ann", DeptA, ("2026-10-26", "present"));
        var b = Member("Bob", DeptB, ("2026-10-26", "absent"));
        TodayStatus(a, "working");
        TodayStatus(b, "absent");
        _summary = new TeamTodaySummary(2, 1, 0, 0, 0, 1, 0, 0, 0, 0);

        var r = (await Handler().Handle(Week(department: DeptA), CancellationToken.None)).Value!;

        r.TeamSize.Should().Be(1);
        r.TodayCounts.Should().Be(new TeamAttendanceCounts(1, 0, 0, 0, 0));
        r.PeriodCounts.Absent.Should().Be(0);
        r.Departments.Should().BeEquivalentTo([new TeamAttendanceDepartment(DeptA, "Alpha"), new TeamAttendanceDepartment(DeptB, "Beta")]);
    }

    [Fact]
    public async Task Summary_UnknownDepartment_ReturnsEmptyNotOthers()
    {
        Member("Ann", DeptA, ("2026-10-26", "present"));
        TodayStatus(_covered[0], "working");

        var r = (await Handler().Handle(Week(department: Guid.NewGuid()), CancellationToken.None)).Value!;

        r.TeamSize.Should().Be(0);
        r.TodayPreview.Should().BeEmpty();
        r.PeriodCounts.Should().Be(new TeamAttendanceCounts(0, 0, 0, 0, 0));
        r.PeriodRate.Should().BeNull();
    }

    [Fact]
    public async Task Summary_ExcludesActor()
    {
        Member("Ann", null);

        await Handler().Handle(Week(), CancellationToken.None);

        _reader.Verify(r => r.LoadAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => !ids.Contains(_actor.Id) && ids.Count == 1),
            new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 1), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Summary_PreviewOrderedProblemsFirst_Max6()
    {
        var names = new[] { "Hal", "Gus", "Fay", "Eve", "Dan", "Cat", "Bob", "Ann" };
        var statuses = new[] { "working", "on_leave", "not_started", "absent", "working", "clocked_out", "absent", "not_scheduled" };
        for (var i = 0; i < names.Length; i++)
        {
            var id = Member(names[i], null);
            TodayStatus(id, statuses[i], isLate: names[i] == "Cat");
        }

        var r = (await Handler().Handle(Week(), CancellationToken.None)).Value!;

        r.TodayPreview.Select(p => p.Name).Should().Equal("Cat", "Bob", "Eve", "Fay", "Dan", "Hal");
        r.TodayPreview.First().Bucket.Should().Be(TeamAttendanceBuckets.Late);
    }

    [Fact]
    public async Task Employees_MonthHasDaysInMonthCells()
    {
        Member("Ann", null, ("2026-09-01", "present"));
        _days[0] = _days[0] with { BucketByDate = Feb2028() };

        var r = await Handler().Handle(new GetTeamAttendanceEmployeesQuery("month", new DateOnly(2028, 2, 10), null, null, 1, 20), CancellationToken.None);

        r.Value!.Items.Single().Days.Should().HaveCount(29);
    }

    private static Dictionary<DateOnly, string> Feb2028()
    {
        var map = new Dictionary<DateOnly, string>();
        for (var d = new DateOnly(2028, 1, 1); d <= new DateOnly(2028, 2, 29); d = d.AddDays(1)) map[d] = "present";
        return map;
    }

    [Fact]
    public async Task Employees_SearchAndPaging()
    {
        for (var i = 0; i < 60; i++) Member($"Emp{i:00}", null, ("2026-10-26", "present"));
        Member("Zed Special", null, ("2026-10-26", "absent"));

        var paged = (await Handler().Handle(new GetTeamAttendanceEmployeesQuery("week", null, null, null, 1, 100), CancellationToken.None)).Value!;
        paged.PageSize.Should().Be(50);
        paged.Items.Should().HaveCount(50);
        paged.TotalCount.Should().Be(61);
        paged.Items.First().Name.Should().Be("Emp00");

        var search = (await Handler().Handle(new GetTeamAttendanceEmployeesQuery("week", null, null, "special", 1, 20), CancellationToken.None)).Value!;
        search.Items.Should().ContainSingle(row => row.Name == "Zed Special");
        var zed = search.Items.Single();
        zed.Attended.Should().Be(0);
        zed.EligibleDays.Should().Be(1);
        zed.Rate.Should().Be(0m);
        zed.Days.Should().HaveCount(7);
    }
}
