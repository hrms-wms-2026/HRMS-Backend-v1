using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.ObjectiveChangeRequests;

public class RequestAllocationExtensionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ReportingManagerId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (RequestAllocationExtensionCommandHandler Handler, Mock<IObjectiveChangeRequestRepository> Requests) Build(Guid? reportingManagerId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        var objective = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, OwnerId = EmployeeId, ReportingManagerId = reportingManagerId,
            IsActive = true, AllocatedHours = 60m, CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeRequestResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new RequestAllocationExtensionCommandHandler(currentUser.Object, identity.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_HasReportingManager_CreatesPendingRequest()
    {
        var (handler, requests) = Build(reportingManagerId: ReportingManagerId);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "Need more hours for the new scope");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.AddAsync(
            It.Is<ObjectiveChangeRequest>(
                r => r.RequestType == ObjectiveChangeRequestTypes.ExtendAllocation
                     && r.ReportingManagerId == ReportingManagerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RootObjectiveNoReportingManager_ReturnsBadRequest()
    {
        var (handler, requests) = Build(reportingManagerId: null);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "reason");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        requests.Verify(x => x.AddAsync(It.IsAny<ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
