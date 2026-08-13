using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListMyObjectiveChangeRequestsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsPendingRequestsForCaller()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(ManagerId);

        var pending = new List<ObjectiveChangeRequest>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = Guid.NewGuid(), RequestType = "delete", ReportingManagerId = ManagerId, Status = "pending", CreatedAt = DateTimeOffset.UtcNow }
        };

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.ListPendingForApproverAsync(TenantId, ManagerId, It.IsAny<CancellationToken>())).ReturnsAsync(pending);

        var handler = new ListMyObjectiveChangeRequestsQueryHandler(currentUser.Object, requests.Object);

        var result = await handler.Handle(new ListMyObjectiveChangeRequestsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        var handler = new ListMyObjectiveChangeRequestsQueryHandler(currentUser.Object, requests.Object);

        var result = await handler.Handle(new ListMyObjectiveChangeRequestsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
