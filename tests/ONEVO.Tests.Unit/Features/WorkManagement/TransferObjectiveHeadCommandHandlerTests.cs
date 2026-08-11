using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class TransferObjectiveHeadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid NewHeadId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static TransferObjectiveHeadCommand ValidCommand() => new(ObjectiveId, NewHeadId);

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        // Defaults so the pre-existing tests below (written before membership sync / auto-grant
        // existed) keep passing unmodified: the resolved new head always resolves as an active
        // employee, and no particular membership/auto-grant behavior is asserted. Same pattern as
        // CreateObjectiveCommandHandlerTests.BuildHandler.
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = NewHeadId, EmploymentStatusId = EmploymentStatusIds.Active });

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new TransferObjectiveHeadCommandHandler(
            currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, requests);
    }

    // Overload without `newHeadAssignee`: defaults to "resolved new head is a valid active
    // employee" so callers that don't care about employee-validity behavior get the happy path.
    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? objective, bool oldHeadHasOtherAccess = false)
        => BuildHandlerWithMembership(
            objective,
            new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = NewHeadId, EmploymentStatusId = EmploymentStatusIds.Active },
            oldHeadHasOtherAccess);

    // Overload with an explicit `newHeadAssignee`: used as-is, including `null`, so a caller can
    // simulate "no active employee found" (see Handle_NewHeadNotActiveEmployee_ReturnsBadRequest).
    // Deviation from the task-7 brief: the brief showed a single method with
    // `Employee? newHeadAssignee = null` and a `newHeadAssignee ?? new Employee {...}` fallback
    // inside. That can't distinguish "caller omitted the parameter" from "caller explicitly wants
    // no active employee" - both hit the same `null`, so the `??` always substitutes a valid
    // employee and Handle_NewHeadNotActiveEmployee_ReturnsBadRequest could never actually observe
    // GetActiveAssigneeAsync returning null. Same class of bug Task 6 found and fixed in
    // CreateObjectiveCommandHandlerTests.BuildHandlerWithMembership; fixed here the same way, by
    // splitting into two overloads instead of one method with a nullable optional parameter.
    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? objective, Employee? newHeadAssignee, bool oldHeadHasOtherAccess = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHeadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newHeadAssignee);
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldHeadHasOtherAccess);

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new TransferObjectiveHeadCommandHandler(
            currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, membership, autoGrant);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_UpsertsNewHeadMembershipAndDeactivatesOld()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), oldHeadHasOtherAccess: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, NewHeadId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OldHeadHasNoOtherAccess_DropsThemFromProject()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), oldHeadHasOtherAccess: false);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        membership.Verify(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadId, ObjectiveId, It.IsAny<CancellationToken>()), Times.Once);
        // DeactivateMembershipAsync on THIS objective already ran regardless (verified above) -
        // HasOtherActiveAccessAsync being checked at all is what "drop from project" reduces to,
        // since deactivating the one membership row IS the full removal when there's no other row.
    }

    [Fact]
    public async Task Handle_NewHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), newHeadAssignee: null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_CascadesReportingManagerToDirectChildren()
    {
        var child = SubObjective(createdById: OtherUserId);
        child.Id = Guid.NewGuid();
        child.ParentObjectiveId = ObjectiveId;
        child.ReportingManagerId = HeadId;

        var (handler, objectives, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId));
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { child });

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewHeadId, child.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_EnsuresProjectsAccessGrantedForNewHead()
    {
        var (handler, _, _, autoGrant) = BuildHandlerWithMembership(SubObjective(createdById: HeadId));

        await handler.Handle(ValidCommand(), CancellationToken.None);

        autoGrant.Verify(x => x.EnsureGrantedAsync(TenantId, NewHeadId, HeadId, "projects:access", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_DoesNotTouchMembershipYet()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadId)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_CreatesPendingRequest()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.Is<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
            r => r.RequestType == "transfer" && r.PayloadJson!.Contains(NewHeadId.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
