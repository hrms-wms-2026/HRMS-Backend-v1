using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class DetectOfflineAgentsJobTests
{
    [Fact]
    public async Task When_no_offline_agents_outbox_is_not_written()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.MarkAgentsInactiveAndReturnIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var outbox = new Mock<IOutboxWriter>();
        var uow = new Mock<IUnitOfWork>();

        var agentIds = await repo.Object.MarkAgentsInactiveAndReturnIdsAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5), default);

        agentIds.Should().BeEmpty();
        outbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_agents_go_offline_outbox_event_written_per_agent()
    {
        var offlineIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.MarkAgentsInactiveAndReturnIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(offlineIds);

        var outbox = new Mock<IOutboxWriter>();
        var uow = new Mock<IUnitOfWork>();

        var agentIds = await repo.Object.MarkAgentsInactiveAndReturnIdsAsync(
            DateTimeOffset.UtcNow.AddMinutes(-5), default);

        agentIds.Count.Should().Be(2);

        foreach (var agentId in agentIds)
            await outbox.Object.EnqueueAsync("AgentHeartbeatLost", new { agent_id = agentId });

        await uow.Object.SaveChangesAsync(default);

        outbox.Verify(o => o.EnqueueAsync(
            "AgentHeartbeatLost",
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
