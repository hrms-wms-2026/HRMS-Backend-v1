using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>GET led-progress/weekly-completion handler: gate, weeks, scope narrowing, allocation, zero fill.</summary>
public sealed class GetLedWeeklyCompletionQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkLeadershipService> _leadership = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ILedProgressReadRepository> _reads = new();
    // Wednesday 2026-10-21 (UTC): current week Mon 19 .. Sun 25 Oct.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero) };
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Employee _caller;

    // Scope: project P1 head A (100 h, child B 40 h); project P2 head D (30 h).
    private readonly Guid _p1 = Guid.NewGuid(), _p2 = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid(), _d = Guid.NewGuid();
    private readonly List<Guid> _liveProjects;

    private IReadOnlyCollection<Guid>? _ids;
    private LedDueWindow? _span;

    public GetLedWeeklyCompletionQueryHandlerTests()
    {
        _liveProjects = [_p1, _p2];
        _caller = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId };
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_caller);

        var end = new DateOnly(2026, 12, 31);
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedWorkScope(
            [
                new LedHeadModule(_a, _p1, "A", false, end, new HashSet<Guid> { _a, _b }, 100,
                    [new(_a, _p1, null, "A", false, end, 100), new(_b, _p1, _a, "B", false, end, 40)]),
                new LedHeadModule(_d, _p2, "D", false, end, new HashSet<Guid> { _d }, 30, [new(_d, _p2, null, "D", false, end, 30)]),
            ], []));

        _projects.Setup(p => p.ListByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(_liveProjects.Contains).Select(id => new Project { Id = id, TenantId = _tenantId, Name = "P" }).ToList());

        _reads.Setup(r => r.GetCompletionsByDayAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<LedDueWindow>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, LedDueWindow, CancellationToken>((_, ids, span, _) =>
            {
                _ids = ids;
                _span = span;
            })
            .ReturnsAsync(new LedCompletionRows(
            [
                new LedCompletionDayRow(new DateOnly(2026, 8, 17), 2, 1),  // first Monday of 10 weeks
                new LedCompletionDayRow(new DateOnly(2026, 10, 12), 3, 2),
                new LedCompletionDayRow(new DateOnly(2026, 10, 21), 4.5m, 1),
            ], 6, 2));
    }

    private GetLedWeeklyCompletionQueryHandler Handler()
        => new(_user.Object, _clock, _employees.Object, _leadership.Object, _projects.Object, _reads.Object);

    [Fact]
    public async Task Unauthenticated_or_tenantless_caller_is_forbidden()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(false);
        (await Handler().Handle(new GetLedWeeklyCompletionQuery(), default)).StatusCode.Should().Be(403);

        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Guid.Empty);
        (await Handler().Handle(new GetLedWeeklyCompletionQuery(), default)).StatusCode.Should().Be(403);
        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Default_ten_weeks_bucket_the_rows_and_report_undated_and_allocation()
    {
        var r = (await Handler().Handle(new GetLedWeeklyCompletionQuery(), default)).Value!;

        r.Weeks.Should().HaveCount(10);
        (r.Weeks[0].WeekStart, r.Weeks[0].WeekEnd, r.Weeks[0].CompletedHours, r.Weeks[0].CompletedTaskCount)
            .Should().Be((new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23), 2m, 1));
        (r.Weeks[8].WeekStart, r.Weeks[8].CompletedHours, r.Weeks[8].CompletedTaskCount).Should().Be((new DateOnly(2026, 10, 12), 3m, 2));
        (r.Weeks[9].WeekStart, r.Weeks[9].WeekEnd, r.Weeks[9].CompletedHours).Should().Be((new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25), 4.5m));
        r.Weeks.Skip(1).Take(7).Should().OnlyContain(w => w.CompletedHours == 0 && w.CompletedTaskCount == 0);
        (r.UndatedCompletedHours, r.UndatedCompletedTaskCount).Should().Be((6m, 2));
        r.AllocatedHours.Should().Be(130m, "the heads' allocations, A 100 + D 30 - B is part of A's");

        _span.Should().Be(new LedDueWindow(new DateOnly(2026, 8, 17), new DateOnly(2026, 10, 25)));
        _ids.Should().BeEquivalentTo(new[] { _a, _b, _d });
    }

    [Fact]
    public async Task Weeks_parameter_sets_the_span()
    {
        var r = (await Handler().Handle(new GetLedWeeklyCompletionQuery(Weeks: 2), default)).Value!;

        r.Weeks.Select(w => w.WeekStart).Should().Equal(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19));
        _span.Should().Be(new LedDueWindow(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public async Task Project_and_objective_filters_narrow_the_scope_and_the_allocation()
    {
        var byProject = (await Handler().Handle(new GetLedWeeklyCompletionQuery(ProjectId: _p2), default)).Value!;
        _ids.Should().BeEquivalentTo(new[] { _d });
        byProject.AllocatedHours.Should().Be(30m);

        var byChild = (await Handler().Handle(new GetLedWeeklyCompletionQuery(ObjectiveId: _b), default)).Value!;
        _ids.Should().BeEquivalentTo(new[] { _b });
        byChild.AllocatedHours.Should().Be(40m, "the child Module's own allocation");

        var byHead = (await Handler().Handle(new GetLedWeeklyCompletionQuery(ProjectId: _p1, ObjectiveId: _a), default)).Value!;
        _ids.Should().BeEquivalentTo(new[] { _a, _b });
        byHead.AllocatedHours.Should().Be(100m, "the head's allocation already includes its child's");
    }

    [Fact]
    public async Task Head_of_a_project_the_lookup_no_longer_returns_is_left_out_of_the_allocation_like_led_progress()
    {
        _liveProjects.Remove(_p2);

        var r = (await Handler().Handle(new GetLedWeeklyCompletionQuery(), default)).Value!;

        r.AllocatedHours.Should().Be(100m);
    }

    [Fact]
    public async Task Out_of_scope_filter_gives_zero_filled_weeks_and_no_query()
    {
        var foreign = Guid.NewGuid();

        foreach (var query in new[]
                 {
                     new GetLedWeeklyCompletionQuery(ProjectId: foreign),
                     new GetLedWeeklyCompletionQuery(ObjectiveId: foreign),
                     new GetLedWeeklyCompletionQuery(ProjectId: _p2, ObjectiveId: _b),
                 })
        {
            var r = (await Handler().Handle(query, default)).Value!;
            r.Weeks.Should().HaveCount(10).And.OnlyContain(w => w.CompletedHours == 0 && w.CompletedTaskCount == 0);
            r.Weeks[^1].WeekStart.Should().Be(new DateOnly(2026, 10, 19));
            (r.AllocatedHours, r.UndatedCompletedHours, r.UndatedCompletedTaskCount).Should().Be((0m, 0m, 0));
        }

        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Caller_without_legal_entity_or_led_modules_gets_zero_filled_weeks()
    {
        _caller.LegalEntityId = null;
        var noEntity = (await Handler().Handle(new GetLedWeeklyCompletionQuery(Weeks: 4), default)).Value!;
        (noEntity.Weeks.Count, noEntity.AllocatedHours).Should().Be((4, 0m));

        _caller.LegalEntityId = _legalEntityId;
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LedWorkScope.Empty);
        var noScope = (await Handler().Handle(new GetLedWeeklyCompletionQuery(), default)).Value!;
        (noScope.Weeks.Count, noScope.Weeks.Sum(w => w.CompletedHours), noScope.AllocatedHours).Should().Be((10, 0m, 0m));

        _reads.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(26, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(27, false)]
    public void Validator_accepts_1_to_26_weeks(int weeks, bool valid)
        => new GetLedWeeklyCompletionQueryValidator().Validate(new GetLedWeeklyCompletionQuery(weeks)).IsValid.Should().Be(valid);

    [Fact]
    public void Default_weeks_is_10()
        => new GetLedWeeklyCompletionQuery().Weeks.Should().Be(10);
}
