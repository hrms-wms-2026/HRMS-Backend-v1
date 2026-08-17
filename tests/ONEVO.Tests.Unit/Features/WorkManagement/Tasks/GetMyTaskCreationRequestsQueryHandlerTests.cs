using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskCreationRequests;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyTaskCreationRequestsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsPendingRequestsForOwner()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);

        var payload = new ONEVO.Application.Features.WorkManagement.Tasks.DTOs.TaskCreationRequestPayload(
            "New task", null, "task", "medium", null, 5m, null, Guid.NewGuid());
        var pending = new List<TaskCreationRequest>
        {
            new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = Guid.NewGuid(),
                RequestedByEmployeeId = Guid.NewGuid(),
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
                Status = TaskCreationRequestStatuses.Pending, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var requests = new Mock<ITaskCreationRequestRepository>();
        requests.Setup(x => x.GetPendingForOwnerEmployeeIdAsync(TenantId, OwnerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var handler = new GetMyTaskCreationRequestsQueryHandler(currentUser.Object, identity.Object, requests.Object);
        var result = await handler.Handle(new GetMyTaskCreationRequestsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("New task", result.Value![0].Payload.Title);
    }
}
