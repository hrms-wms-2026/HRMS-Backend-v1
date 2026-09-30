using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Queries;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team;

public sealed class GetTeamActionItemsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegalEntityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed class FakeSource(
        string key, string domain, bool gated,
        Func<Task<ActionSourceSummary>>? summary = null,
        Exception? gateThrows = null) : ITeamActionSource
    {
        public string Key => key;
        public string Domain => domain;
        public int SummaryCalls { get; private set; }
        public int LastTop { get; private set; }
        public Guid LastLegalEntityId { get; private set; }

        public Task<bool> IsGatedAsync(CancellationToken ct = default)
            => gateThrows is not null ? throw gateThrows : Task.FromResult(gated);

        public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
        {
            SummaryCalls++;
            LastTop = top;
            LastLegalEntityId = legalEntityId;
            return summary is not null
                ? await summary()
                : new ActionSourceSummary(key, domain, ActionSourceSummary.StatusOk, 0, null, null, []);
        }
    }

    [Fact]
    public async Task Forbidden_when_not_authenticated()
    {
        var handler = CreateHandler(isAuthenticated: false, []);
        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NotFound_when_actor_has_no_legal_entity()
    {
        var handler = CreateHandler(isAuthenticated: true, [], hasLegalEntity: false);
        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Gated_out_source_is_omitted_entirely_not_returned_as_zero()
    {
        var gatedOut = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: false);
        var handler = CreateHandler(isAuthenticated: true, [gatedOut]);

        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        Assert.Empty(result.Value!.Sources);
        Assert.Equal(0, gatedOut.SummaryCalls);
    }

    [Fact]
    public async Task Gated_in_source_summary_is_returned_as_is()
    {
        var source = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: true,
            summary: () => Task.FromResult(new ActionSourceSummary(
                "leave.approval", ActionSourceSummary.DomainPeople, ActionSourceSummary.StatusOk, 3, null,
                DateTimeOffset.Parse("2026-09-01T00:00:00Z"), [])));
        var handler = CreateHandler(isAuthenticated: true, [source]);

        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        var only = Assert.Single(result.Value!.Sources);
        Assert.Equal("leave.approval", only.SourceKey);
        Assert.Equal(3, only.PendingCount);
        Assert.Equal(ActionSourceSummary.StatusOk, only.Status);
    }

    [Fact]
    public async Task A_source_whose_gate_check_throws_is_reported_unavailable_and_others_still_return()
    {
        var broken = new FakeSource("monitoring.exception", ActionSourceSummary.DomainPeople, gated: true,
            gateThrows: new InvalidOperationException("boom"));
        var healthy = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: true);
        var handler = CreateHandler(isAuthenticated: true, [broken, healthy]);

        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value!.Sources.Count);
        var brokenSummary = result.Value.Sources.Single(s => s.SourceKey == "monitoring.exception");
        Assert.Equal(ActionSourceSummary.StatusUnavailable, brokenSummary.Status);
        Assert.Equal(0, brokenSummary.PendingCount);
        var healthySummary = result.Value.Sources.Single(s => s.SourceKey == "leave.approval");
        Assert.Equal(ActionSourceSummary.StatusOk, healthySummary.Status);
    }

    [Fact]
    public async Task A_source_whose_summary_call_throws_is_reported_unavailable_and_others_still_return()
    {
        var broken = new FakeSource("work.task_creation", ActionSourceSummary.DomainWork, gated: true,
            summary: () => throw new InvalidOperationException("boom"));
        var healthy = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: true);
        var handler = CreateHandler(isAuthenticated: true, [broken, healthy]);

        var result = await handler.Handle(new GetTeamActionItemsQuery(), CancellationToken.None);

        var brokenSummary = result.Value!.Sources.Single(s => s.SourceKey == "work.task_creation");
        Assert.Equal(ActionSourceSummary.StatusUnavailable, brokenSummary.Status);
        Assert.Equal(ActionSourceSummary.DomainWork, brokenSummary.Domain);
        Assert.Contains(result.Value.Sources, s => s.SourceKey == "leave.approval" && s.Status == ActionSourceSummary.StatusOk);
    }

    [Fact]
    public async Task Passes_top_per_source_and_the_actors_legal_entity_to_every_gated_in_source()
    {
        var source = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: true);
        var handler = CreateHandler(isAuthenticated: true, [source]);

        await handler.Handle(new GetTeamActionItemsQuery(TopPerSource: 3), CancellationToken.None);

        Assert.Equal(3, source.LastTop);
        Assert.Equal(LegalEntityId, source.LastLegalEntityId);
    }

    [Fact]
    public async Task Non_positive_top_per_source_falls_back_to_five()
    {
        var source = new FakeSource("leave.approval", ActionSourceSummary.DomainPeople, gated: true);
        var handler = CreateHandler(isAuthenticated: true, [source]);

        await handler.Handle(new GetTeamActionItemsQuery(TopPerSource: 0), CancellationToken.None);

        Assert.Equal(5, source.LastTop);
    }

    private static GetTeamActionItemsQueryHandler CreateHandler(
        bool isAuthenticated, IEnumerable<ITeamActionSource> sources, bool hasLegalEntity = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = Guid.NewGuid(), UserId = UserId, TenantId = TenantId,
                LegalEntityId = hasLegalEntity ? LegalEntityId : null,
            });

        return new GetTeamActionItemsQueryHandler(
            currentUser.Object, employees.Object, sources, NullLogger<GetTeamActionItemsQueryHandler>.Instance);
    }
}
