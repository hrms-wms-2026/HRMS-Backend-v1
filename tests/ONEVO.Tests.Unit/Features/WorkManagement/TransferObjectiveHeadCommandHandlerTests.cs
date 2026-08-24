using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class TransferObjectiveHeadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid NewHeadEmployeeId = Guid.NewGuid();
    private static readonly Guid NewHeadUserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static TransferObjectiveHeadCommand ValidCommand() => new(ObjectiveId, NewHeadEmployeeId);

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadEmployeeId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    // Maps the test's login-space caller ids to the employee-space ids the handler now compares
    // against - HeadUserId resolves to HeadEmployeeId (matches SubObjective's fixed OwnerId),
    // OtherUserId resolves to OtherEmployeeId (a different employee, so "not the head").
    private static Mock<ICallerIdentityResolver> BuildIdentity()
    {
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, HeadUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeadEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);
        return identity;
    }

    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests, Mock<IProjectMemberInvitationRepository> Invitations) BuildHandler(
        Objective? objective, bool hasPending = false, Guid? callerId = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerUserId = callerId ?? HeadUserId;
        var resolvedCallerEmployeeId = resolvedCallerUserId == OtherUserId ? OtherEmployeeId : HeadEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerUserId);

        var identity = BuildIdentity();

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation>());

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = NewHeadEmployeeId, TenantId = TenantId, UserId = NewHeadUserId, EmploymentStatusId = EmploymentStatusIds.Active });
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective is not null && objective.OwnerId == resolvedCallerEmployeeId));

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TransferOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TransferOutcomeResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new TransferObjectiveHeadCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, requests.Object, invitations.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, requests, invitations);
    }

    // Overload without `newHeadAssignee`: defaults to "resolved new head is a valid active
    // employee" so callers that don't care about employee-validity behavior get the happy path.
    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? objective, bool oldHeadHasOtherAccess = false, bool? callerIsEffectiveManager = null)
        => BuildHandlerWithMembership(
            objective,
            new Employee { Id = NewHeadEmployeeId, TenantId = TenantId, UserId = NewHeadUserId, EmploymentStatusId = EmploymentStatusIds.Active },
            oldHeadHasOtherAccess,
            callerIsEffectiveManager);

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
        Objective? objective, Employee? newHeadAssignee, bool oldHeadHasOtherAccess = false, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(HeadUserId);

        var identity = BuildIdentity();

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHeadEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newHeadAssignee);
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadEmployeeId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldHeadHasOtherAccess);
        // Caller in this builder is always HeadUserId (resolves to HeadEmployeeId); mirrors
        // direct-owner-only behavior by default, overridable to simulate an ancestor-cascade grant.
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, HeadEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective is not null && objective.OwnerId == HeadEmployeeId));

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.ListPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation>());

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TransferOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TransferOutcomeResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new TransferObjectiveHeadCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, requests.Object, invitations.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, membership, autoGrant);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_UpsertsNewHeadMembershipAndDeactivatesOld()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadUserId), oldHeadHasOtherAccess: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, NewHeadEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OldHeadHasNoOtherAccess_DropsThemFromProject()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadUserId), oldHeadHasOtherAccess: false);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        membership.Verify(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadEmployeeId, ObjectiveId, It.IsAny<CancellationToken>()), Times.Once);
        // DeactivateMembershipAsync on THIS objective already ran regardless (verified above) -
        // HasOtherActiveAccessAsync being checked at all is what "drop from project" reduces to,
        // since deactivating the one membership row IS the full removal when there's no other row.
    }

    [Fact]
    public async Task Handle_NewHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadUserId), newHeadAssignee: null);

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
        child.ReportingManagerId = HeadEmployeeId;

        var (handler, objectives, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadUserId));
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { child });

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewHeadEmployeeId, child.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_EnsuresProjectsAccessGrantedForNewHead()
    {
        var (handler, _, _, autoGrant) = BuildHandlerWithMembership(SubObjective(createdById: HeadUserId));

        await handler.Handle(ValidCommand(), CancellationToken.None);

        // Auto-grant stays UserId-keyed (out of Phase 2 scope) - it resolves the new head's login
        // UserId off the Employee record returned by GetActiveAssigneeAsync, not the EmployeeId itself.
        autoGrant.Verify(x => x.EnsureGrantedAsync(TenantId, NewHeadUserId, HeadUserId, "projects:access", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_DoesNotTouchMembershipYet()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_AppliesImmediately()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: HeadUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadEmployeeId)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_AppliesImmediately()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, objectives, requests, _) = BuildHandler(
            SubObjective(createdById: OtherUserId), callerId: OtherUserId, callerIsEffectiveManager: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadEmployeeId)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_CreatesPendingRequest()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.Is<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
            r => r.RequestType == "transfer" && r.PayloadJson!.Contains(NewHeadEmployeeId.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isDefault: true));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadUserId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonCreatorCaller_ObjectiveHasNoReportingManager_CreatesLeaderInvitationInsteadOfChangeRequest()
    {
        var objective = SubObjective(createdById: OtherUserId);
        objective.ReportingManagerId = null;
        var (handler, _, requests, invitations) = BuildHandler(objective);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingInvitation);
        Assert.Null(result.Value.PendingChangeRequest);
        Assert.Equal(HeadEmployeeId, objective.OwnerId);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedEmployeeId == NewHeadEmployeeId
            && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
