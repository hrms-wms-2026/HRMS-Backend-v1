using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ApproveObjectiveChangeRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerUserId = Guid.NewGuid();
    private static readonly Guid ManagerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid NewHeadEmployeeId = Guid.NewGuid();
    private static readonly Guid ParentObjectiveId = Guid.NewGuid();

    private static Objective TargetObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, Title = "Sub", OwnerId = Guid.NewGuid(),
        ReportingManagerId = ManagerEmployeeId, IsActive = true,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private static ObjectiveChangeRequest DeleteRequest(string status = ObjectiveChangeRequestStatuses.Pending) => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Delete,
        ReportingManagerId = ManagerEmployeeId, Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ObjectiveChangeRequest TransferRequest() => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Transfer,
        ReportingManagerId = ManagerEmployeeId, Status = ObjectiveChangeRequestStatuses.Pending,
        PayloadJson = JsonSerializer.Serialize(new TransferObjectiveRequestPayload(NewHeadEmployeeId)), CreatedAt = DateTimeOffset.UtcNow
    };

    private static Mock<ICallerIdentityResolver> BuildIdentity()
    {
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, ManagerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagerEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);
        return identity;
    }

    private (ApproveObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        ObjectiveChangeRequest? request, Objective? objective, Guid? callerId = null)
    {
        var (handler, objectives, requests, _) = BuildHandlerWithMembership(request, objective, callerId: callerId);
        return (handler, objectives, requests);
    }

    private (ApproveObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandlerWithMembership(
        ObjectiveChangeRequest? request, Objective? objective, List<Objective>? directChildren = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? ManagerUserId);

        var identity = BuildIdentity();

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(directChildren ?? new List<Objective>());

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId });
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((operation, innerCt) => operation(innerCt));

        var handler = new ApproveObjectiveChangeRequestCommandHandler(
            currentUser.Object, identity.Object, requests.Object, objectives.Object, membership.Object,
            new ObjectiveAllocationSlackCalculator(objectives.Object, new Mock<IWorkTaskRepository>().Object),
            new Mock<INotificationDispatcher>().Object, unitOfWork.Object);
        return (handler, objectives, requests, membership);
    }

    [Fact]
    public async Task Handle_ApproveDelete_SoftDeletesObjectiveAndMarksApproved()
    {
        var (handler, objectives, requests) = BuildHandler(DeleteRequest(), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsActive)), Times.Once);
        requests.Verify(x => x.Update(It.Is<ObjectiveChangeRequest>(r => r.Status == ObjectiveChangeRequestStatuses.Approved)), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveTransfer_ReassignsOwnerIdFromPayload()
    {
        var (handler, objectives, _) = BuildHandler(TransferRequest(), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadEmployeeId)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotReportingManager_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(DeleteRequest(), TargetObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(DeleteRequest(status: ObjectiveChangeRequestStatuses.Approved), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RequestNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ApproveTransfer_SyncsMembershipAndCascadesReportingManager()
    {
        var child = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ParentObjectiveId = ObjectiveId, IsActive = true, ReportingManagerId = Guid.NewGuid() };
        var (handler, objectives, _, membership) = BuildHandlerWithMembership(TransferRequest(), TargetObjective(), directChildren: new List<Objective> { child });

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewHeadEmployeeId, child.ReportingManagerId);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, NewHeadEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveAchieve_SetsIsAchievedAndDeactivatesHeadMembership()
    {
        var achieveRequest = new ObjectiveChangeRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Achieve,
            ReportingManagerId = ManagerEmployeeId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objectives, _, membership) = BuildHandlerWithMembership(achieveRequest, TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved && o.AchievedAt != null)), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveUnachieve_ClearsIsAchievedAndRestoresHeadMembership()
    {
        var unachieveRequest = new ObjectiveChangeRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Unachieve,
            ReportingManagerId = ManagerEmployeeId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        var achievedTarget = TargetObjective();
        achievedTarget.IsAchieved = true;
        achievedTarget.AchievedAt = DateTimeOffset.UtcNow;
        var (handler, objectives, _, membership) = BuildHandlerWithMembership(unachieveRequest, achievedTarget);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsAchieved && o.AchievedAt == null)), Times.Once);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Objective ChildObjective(decimal allocatedHours) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ParentObjectiveId = ParentObjectiveId,
        OwnerId = Guid.NewGuid(), ReportingManagerId = ManagerEmployeeId,
        AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective ApproverObjective(decimal allocatedHours) => new()
    {
        Id = ParentObjectiveId, TenantId = TenantId, OwnerId = ManagerEmployeeId,
        AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ObjectiveChangeRequest ExtendAllocationRequest(Guid objectiveId, decimal requestedAdditionalHours) => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = objectiveId,
        RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
        ReportingManagerId = ManagerEmployeeId, Status = ObjectiveChangeRequestStatuses.Pending,
        PayloadJson = JsonSerializer.Serialize(new ExtendAllocationRequestPayload(requestedAdditionalHours, "Need more hours")),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private (ApproveObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveRepository> Objectives) BuildWithSlack(
        ObjectiveChangeRequest changeRequest, Objective childObjective, Objective approverObjective, decimal approverSlack)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(ManagerUserId);

        var identity = BuildIdentity();

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(changeRequest);

        var childSum = approverObjective.AllocatedHours - approverSlack;
        var siblingRemainder = childSum - childObjective.AllocatedHours;
        var children = new List<Objective> { childObjective };
        if (siblingRemainder > 0)
            children.Add(new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ParentObjectiveId = approverObjective.Id, AllocatedHours = siblingRemainder, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, childObjective.Id, It.IsAny<CancellationToken>())).ReturnsAsync(childObjective);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, approverObjective.Id, It.IsAny<CancellationToken>())).ReturnsAsync(approverObjective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, approverObjective.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, approverObjective.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((operation, innerCt) => operation(innerCt));

        var handler = new ApproveObjectiveChangeRequestCommandHandler(
            currentUser.Object, identity.Object, requests.Object, objectives.Object, membership.Object,
            new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object),
            new Mock<INotificationDispatcher>().Object, unitOfWork.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_ExtendAllocation_ApproverHasEnoughSlack_IncreasesChildAllocationOnly()
    {
        var childObjective = ChildObjective(allocatedHours: 60m);
        var approverObjective = ApproverObjective(allocatedHours: 100m);
        var (handler, objectives) = BuildWithSlack(
            changeRequest: ExtendAllocationRequest(childObjective.Id, requestedAdditionalHours: 20m),
            childObjective, approverObjective, approverSlack: 40m);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.Id == childObjective.Id && o.AllocatedHours == 80m)), Times.Once);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.Id == approverObjective.Id)), Times.Never);
    }

    [Fact]
    public async Task Handle_ExtendAllocation_ApproverInsufficientSlack_ReturnsConflictAndLeavesRequestPending()
    {
        var childObjective = ChildObjective(allocatedHours: 60m);
        var approverObjective = ApproverObjective(allocatedHours: 100m);
        var (handler, objectives) = BuildWithSlack(
            changeRequest: ExtendAllocationRequest(childObjective.Id, requestedAdditionalHours: 20m),
            childObjective, approverObjective, approverSlack: 10m);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
    }
}
