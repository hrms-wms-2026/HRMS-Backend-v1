using Moq;
using ONEVO.Application.Features.AgentGateway.Queries.GetDeviceChangeStatus;
using ONEVO.Application.Features.AgentGateway.Queries.GetPendingDeviceChanges;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class DeviceChangeQueryHandlerTests
{
    [Fact]
    public async Task Status_PendingCandidate_ReturnsItsOwnRequestStatus()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        var candidate = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            Status = "inactive"
        };
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            RequestedAgentId = candidate.Id,
            Status = "pending",
            RequestedAt = DateTimeOffset.UtcNow
        };
        repo.Setup(r => r.GetAgentByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        repo.Setup(r => r.GetDeviceChangeRequestByRequestedAgentIdAsync(
                candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await new GetDeviceChangeStatusQueryHandler(repo.Object)
            .Handle(new GetDeviceChangeStatusQuery(candidate.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pending", result.Value!.ApprovalStatus);
        Assert.Equal(request.Id, result.Value.RequestId);
        Assert.Equal("inactive", result.Value.DeviceStatus);
    }

    [Fact]
    public async Task PendingList_CapsPageSizeAtOneHundred()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        repo.Setup(r => r.GetPendingDeviceChangesAsync(
                100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new GetPendingDeviceChangesQueryHandler(repo.Object)
            .Handle(new GetPendingDeviceChangesQuery(Page: 2, PageSize: 500), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        repo.Verify(r => r.GetPendingDeviceChangesAsync(
            100, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}
