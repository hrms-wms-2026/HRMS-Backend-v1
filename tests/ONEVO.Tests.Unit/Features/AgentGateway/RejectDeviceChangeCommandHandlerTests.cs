using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.RejectDeviceChange;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class RejectDeviceChangeCommandHandlerTests
{
    [Fact]
    public async Task Handle_PendingRequest_RejectsAndAuditsReviewer()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        var uow = new Mock<IUnitOfWork>();
        var reviewerId = Guid.NewGuid();
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            Status = "pending"
        };
        repo.Setup(r => r.GetDeviceChangeRequestByIdAsync(
                request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectDeviceChangeCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(
            new RejectDeviceChangeCommand(request.Id, "Unrecognized device", reviewerId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("rejected", request.Status);
        Assert.Equal(reviewerId, request.ReviewedById);
        Assert.NotNull(request.ReviewedAt);
        Assert.Equal("Unrecognized device", request.ReviewComment);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonPendingRequest_ReturnsConflictWithoutSaving()
    {
        var repo = new Mock<IAgentGatewayRepository>();
        var uow = new Mock<IUnitOfWork>();
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            Status = "approved"
        };
        repo.Setup(r => r.GetDeviceChangeRequestByIdAsync(
                request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var handler = new RejectDeviceChangeCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(
            new RejectDeviceChangeCommand(request.Id, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
