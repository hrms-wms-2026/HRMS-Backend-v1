using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

/// <summary>GET led-progress/period handler: gate, window, scope narrowing, zero totals and mapping.</summary>
public sealed class GetLedPeriodProgressQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkLeadershipService> _leadership = new();
    private readonly Mock<ILedProgressReadRepository> _reads = new();
    // Wednesday 2026-10-21 (UTC).
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero) };
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Employee _caller;

    // Scope: project P1 head A (child B); project P2 head D.
    private readonly Guid _p1 = Guid.NewGuid(), _p2 = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid(), _d = Guid.NewGuid();

    private IReadOnlyCollection<Guid>? _ids;
    private LedDueWindow? _window;

    private static readonly LedDueWindow Week = new(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25));
    private static readonly LedDueWindow Month = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    public GetLedPeriodProgressQueryHandlerTests()
    {
        _caller = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId };
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_caller);

        var end = new DateOnly(2026, 12, 31);
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedWorkScope(
            [
                new LedHeadModule(_a, _p1, "A", false, end, new HashSet<Guid> { _a, _b }, 0,
                    [new(_a, _p1, null, "A", false, end), new(_b, _p1, _a, "B", false, end)]),
                new LedHeadModule(_d, _p2, "D", false, end, new HashSet<Guid> { _d }, 0, [new(_d, _p2, null, "D", false, end)]),
            ], []));

        _reads.Setup(r => r.GetPeriodTotalsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<LedDueWindow>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, LedDueWindow, CancellationToken>((_, ids, window, _) =>
            {
                _ids = ids;
                _window = window;
            })
            .ReturnsAsync(new LedPeriodTotalsRow(5, 3, 2, 10, 6, 2, 1, 3, 1));
    }

    private GetLedPeriodProgressQueryHandler Handler() => new(_user.Object, _clock, _employees.Object, _leadership.Object, _reads.Object);

    [Fact]
    public async Task Unauthenticated_or_tenantless_caller_is_forbidden()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(false);
        (await Handler().Handle(new GetLedPeriodProgressQuery("week"), default)).StatusCode.Should().Be(403);

        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Guid.Empty);
        (await Handler().Handle(new GetLedPeriodProgressQuery("week"), default)).StatusCode.Should().Be(403);
        _reads.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("week")]
    [InlineData("WEEK")]
    public async Task Week_maps_totals_and_the_iso_week_window(string period)
    {
        var result = await Handler().Handle(new GetLedPeriodProgressQuery(period), default);

        var r = result.Value!;
        (r.Period, r.From, r.To).Should().Be(("week", Week.From, Week.To));
        (r.CompletedHours, r.InProgressHours, r.NotStartedHours, r.TotalHours).Should().Be((5m, 3m, 2m, 10m));
        (r.TaskCount, r.CompletedTaskCount, r.InProgressTaskCount, r.NotStartedTaskCount, r.UnestimatedTaskCount)
            .Should().Be((6, 2, 1, 3, 1));
        _window.Should().Be(Week);
        _ids.Should().BeEquivalentTo(new[] { _a, _b, _d });
    }

    [Fact]
    public async Task Month_uses_the_calendar_month()
    {
        var r = (await Handler().Handle(new GetLedPeriodProgressQuery("Month"), default)).Value!;

        (r.Period, r.From, r.To).Should().Be(("month", Month.From, Month.To));
        _window.Should().Be(Month);
    }

    [Fact]
    public async Task Project_and_objective_filters_narrow_the_scope()
    {
        await Handler().Handle(new GetLedPeriodProgressQuery("week", ProjectId: _p2), default);
        _ids.Should().BeEquivalentTo(new[] { _d });

        await Handler().Handle(new GetLedPeriodProgressQuery("week", ObjectiveId: _b), default);
        _ids.Should().BeEquivalentTo(new[] { _b });
    }

    [Fact]
    public async Task Out_of_scope_filter_gives_zero_totals_with_the_window_and_no_query()
    {
        var foreign = Guid.NewGuid();

        foreach (var query in new[]
                 {
                     new GetLedPeriodProgressQuery("month", ProjectId: foreign),
                     new GetLedPeriodProgressQuery("month", ObjectiveId: foreign),
                     new GetLedPeriodProgressQuery("month", ProjectId: _p2, ObjectiveId: _b),
                 })
        {
            var r = (await Handler().Handle(query, default)).Value!;
            (r.Period, r.From, r.To).Should().Be(("month", Month.From, Month.To));
            (r.TotalHours, r.TaskCount, r.UnestimatedTaskCount).Should().Be((0m, 0, 0));
        }

        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Caller_without_legal_entity_or_led_modules_gets_zero_totals()
    {
        _caller.LegalEntityId = null;
        var noEntity = (await Handler().Handle(new GetLedPeriodProgressQuery("week"), default)).Value!;
        (noEntity.From, noEntity.TotalHours).Should().Be((Week.From, 0m));

        _caller.LegalEntityId = _legalEntityId;
        _leadership.Setup(l => l.ResolveLedScopeAsync(_tenantId, _caller.Id, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LedWorkScope.Empty);
        var noScope = (await Handler().Handle(new GetLedPeriodProgressQuery("week"), default)).Value!;
        (noScope.To, noScope.TaskCount).Should().Be((Week.To, 0));

        _reads.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("week", true)]
    [InlineData("MONTH", true)]
    [InlineData(" month ", true)]
    [InlineData("all", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("year", false)]
    public void Validator_accepts_only_week_and_month(string? period, bool valid)
        => new GetLedPeriodProgressQueryValidator().Validate(new GetLedPeriodProgressQuery(period)).IsValid.Should().Be(valid);
}
