using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RejectObjectiveChangeRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerUserId = Guid.NewGuid();
    private static readonly Guid ManagerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static ObjectiveChangeRequest PendingRequest() => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Delete,
        ReportingManagerId = ManagerEmployeeId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
    };

    private (RejectObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        ObjectiveChangeRequest? request, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? ManagerUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, ManagerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagerEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectObjectiveChangeRequestCommandHandler(currentUser.Object, identity.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_Reject_MarksRejectedOnly()
    {
        var (handler, requests) = BuildHandler(PendingRequest());

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<ObjectiveChangeRequest>(r => r.Status == ObjectiveChangeRequestStatuses.Rejected)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotReportingManager_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(PendingRequest(), callerId: OtherUserId);

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RequestNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
