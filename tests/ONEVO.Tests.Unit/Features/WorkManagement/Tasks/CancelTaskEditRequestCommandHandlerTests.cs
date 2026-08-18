using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskEditRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CancelTaskEditRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    private (
        CancelTaskEditRequestCommandHandler Handler,
        Mock<ITaskEditRequestRepository> Requests) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(
                TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var pending = new TaskEditRequest
        {
            Id = RequestId,
            TenantId = TenantId,
            TaskId = Guid.NewGuid(),
            RequestedByEmployeeId = RequesterEmployeeId,
            PayloadJson = "{}",
            Status = TaskEditRequestStatuses.Pending,
            CreatedById = UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var requests = new Mock<ITaskEditRequestRepository>();
        requests.Setup(x => x.GetTrackedByIdForTenantAsync(
                TenantId, RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CancelTaskEditRequestCommandHandler(
            currentUser.Object, identity.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_Requester_CancelsPendingRequest()
    {
        var (handler, requests) = Build(RequesterEmployeeId);

        var result = await handler.Handle(
            new CancelTaskEditRequestCommand(RequestId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<TaskEditRequest>(r =>
            r.Status == TaskEditRequestStatuses.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task Handle_NonRequester_ReturnsForbiddenWithoutUpdating()
    {
        var (handler, requests) = Build(OtherEmployeeId);

        var result = await handler.Handle(
            new CancelTaskEditRequestCommand(RequestId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        requests.Verify(x => x.Update(It.IsAny<TaskEditRequest>()), Times.Never);
    }
}
