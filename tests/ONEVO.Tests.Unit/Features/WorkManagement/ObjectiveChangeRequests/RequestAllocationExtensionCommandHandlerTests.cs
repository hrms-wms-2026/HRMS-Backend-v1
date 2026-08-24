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

    private (RequestAllocationExtensionCommandHandler Handler, Mock<IObjectiveChangeRequestRepository> Requests, Mock<ONEVO.Application.Features.WorkManagement.Objectives.Services.IMilestoneMembershipCoordinator> Membership) Build(Guid? reportingManagerId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [EmployeeId] = "Owner" });

        var objective = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, OwnerId = EmployeeId, ReportingManagerId = reportingManagerId,
            Title = "Child", IsActive = true, AllocatedHours = 60m, CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        var membership = new Mock<ONEVO.Application.Features.WorkManagement.Objectives.Services.IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == EmployeeId));
        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeRequestResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new RequestAllocationExtensionCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, requests.Object,
            membership.Object, notifications.Object, unitOfWork.Object);
        return (handler, requests, membership);
    }

    [Fact]
    public async Task Handle_HasReportingManager_CreatesPendingRequest()
    {
        var (handler, requests, _) = Build(reportingManagerId: ReportingManagerId);
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
        var (handler, requests, _) = Build(reportingManagerId: null);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "reason");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        requests.Verify(x => x.AddAsync(It.IsAny<ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_CreatesPendingRequest()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (cascade) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, requests, _) = Build(reportingManagerId: ReportingManagerId, callerIsEffectiveManager: true);
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
    public async Task Handle_CallerNotEffectiveManager_ReturnsForbidden()
    {
        // Caller is neither this objective's own OwnerId nor an effective manager via any cascade.
        // Verifies that when IsEffectiveManagerAsync returns false, the handler correctly rejects
        // the request with Forbidden.
        var (handler, requests, _) = Build(reportingManagerId: ReportingManagerId, callerIsEffectiveManager: false);
        var command = new RequestAllocationExtensionCommand(ObjectiveId, 20m, "Need more hours for the new scope");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only this milestone's owner can request an allocation extension.", result.Error);
        requests.Verify(x => x.AddAsync(It.IsAny<ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
