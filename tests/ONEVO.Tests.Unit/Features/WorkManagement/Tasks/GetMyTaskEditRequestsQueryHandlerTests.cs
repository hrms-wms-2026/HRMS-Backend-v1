using System.Text.Json;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskEditRequests;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyTaskEditRequestsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid FirstRequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid SecondRequesterEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsPendingRequestsForOwnerWithBatchedRequesterNames()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(
                TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(
                TenantId,
                It.Is<IReadOnlyList<Guid>>(ids =>
                    ids.Count == 2
                    && ids.Contains(FirstRequesterEmployeeId)
                    && ids.Contains(SecondRequesterEmployeeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [FirstRequesterEmployeeId] = "Alex Morgan",
                [SecondRequesterEmployeeId] = "Sam Patel"
            });

        var firstPayload = new TaskEditRequestPayload(
                        "First update", null, WorkTaskPriorities.High, null, null, null, null);

        var secondPayload = new TaskEditRequestPayload(
                        "Second update", null, WorkTaskPriorities.Low, null, null, null, null);

        var pending = new List<TaskEditRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                TaskId = Guid.NewGuid(),
                RequestedByEmployeeId = FirstRequesterEmployeeId,
                PayloadJson = JsonSerializer.Serialize(firstPayload),
                Status = TaskEditRequestStatuses.Pending,
                CreatedById = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                TaskId = Guid.NewGuid(),
                RequestedByEmployeeId = SecondRequesterEmployeeId,
                PayloadJson = JsonSerializer.Serialize(secondPayload),
                Status = TaskEditRequestStatuses.Pending,
                CreatedById = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var requests = new Mock<ITaskEditRequestRepository>();
        requests.Setup(x => x.GetPendingForOwnerEmployeeIdAsync(
                TenantId, OwnerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var handler = new GetMyTaskEditRequestsQueryHandler(
            currentUser.Object, identity.Object, requests.Object);

        var result = await handler.Handle(
            new GetMyTaskEditRequestsQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!,
            item =>
            {
                Assert.Equal("First update", item.Payload.Title);
                Assert.Equal("Alex Morgan", item.RequestedByName);
            },
            item =>
            {
                Assert.Equal("Second update", item.Payload.Title);
                Assert.Equal("Sam Patel", item.RequestedByName);
            });
        identity.Verify(x => x.ResolveDisplayNamesByEmployeeIdAsync(
            TenantId,
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
