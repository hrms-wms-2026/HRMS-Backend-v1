using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
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

public class CreateObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static CreateObjectiveCommand ValidCommand(Guid? headEmployeeId = null, IReadOnlyList<(Guid EmployeeId, string Type)>? memberInvitations = null) => new(
        ParentId, "Sub Milestone", "desc", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1), 20m, headEmployeeId, memberInvitations);

    private static Objective ParentObjective(Guid ownerId, bool isActive = true) => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Parent",
        OwnerId = ownerId, IsActive = isActive, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        AllocatedHours = 40m, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Mock<ICallerIdentityResolver> BuildIdentity()
    {
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
        return identity;
    }

    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(Objective? parent)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = BuildIdentity();

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        // Defaults so the pre-existing tests below (written before membership sync / auto-grant
        // existed) keep passing unmodified: the resolved head always resolves as an active
        // employee, and no particular auto-grant behavior is asserted.
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, CancellationToken _) =>
                new Employee { Id = employeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active });
        // Mirrors direct-owner-only behavior for these pre-existing tests (Moq returns false for
        // any unconfigured IsEffectiveManagerAsync call, which would otherwise flip every
        // caller-is-owner success case to Forbidden now that the handler defers to this method).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ParentId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent is not null && parent.OwnerId == EmployeeId);

        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);
        return (handler, objectives);
    }

    // Overload without `assignee`: defaults to "resolved head is a valid active employee" so
    // callers that don't care about employee-validity behavior (most tests) get the happy path.
    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? parent)
        => BuildHandlerWithMembership(parent, new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active });

    // Overload with an explicit `assignee`: used as-is, including `null`, so a caller can simulate
    // "no active employee found" (see Handle_AssignedHeadNotActiveEmployee_ReturnsBadRequest below).
    // Note: a single method with `Employee? assignee = null` can't distinguish "omitted" from
    // "explicitly null" in C#, which would make that test unwritable - hence the two overloads.
    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? parent, Employee? assignee)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = BuildIdentity();

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee);
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ParentId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent is not null && parent.OwnerId == EmployeeId);

        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);
        return (handler, objectives, membership, autoGrant);
    }

    [Fact]
    public async Task Handle_CallerIsParentHead_CreatesWithSelfAsDefaultHeadAndReportingManager()
    {
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: EmployeeId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.OwnerId);
        Assert.Equal(EmployeeId, result.Value.ReportingManagerId);
        objectives.Verify(x => x.AddAsync(It.Is<Objective>(o => o.OwnerId == EmployeeId && o.ReportingManagerId == EmployeeId && o.ParentObjectiveId == ParentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HeadUserIdInRequestIsIgnored_OwnerAndReportingManagerAreAlwaysCaller()
    {
        // Creator always starts as owner (design amendment) - any HeadUserId on the request is
        // not honored directly by this handler; it is handled entirely by the (separate)
        // member-invitations path, never by assigning ownership here.
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: EmployeeId));

        var result = await handler.Handle(ValidCommand(headEmployeeId: OtherEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.OwnerId);
        Assert.Equal(EmployeeId, result.Value.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CallerNotParentHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: OtherEmployeeId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfGrandparentObjective_CreatesSuccessfully()
    {
        // Caller is neither the direct parent's owner nor a direct member of it, but is an active
        // member of the parent's own ancestor (grandparent, two levels up). IsEffectiveManagerAsync
        // is the single source of truth for that cascade (already unit-tested at the coordinator
        // level in MilestoneMembershipCoordinatorTests) - the handler only needs to prove it defers
        // to the coordinator's answer, so the ancestor walk itself is not re-simulated here.
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = BuildIdentity();
        var parent = ParentObjective(ownerId: OtherEmployeeId);
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, CancellationToken _) =>
                new Employee { Id = employeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ParentId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.OwnerId);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveParent_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: EmployeeId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DatesOutsideParentRange_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: EmployeeId));
        var command = ValidCommand() with { EndDate = new DateOnly(2026, 7, 1) };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_HoursExceedParentTotal_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: EmployeeId));
        var command = ValidCommand() with { AllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidCreate_UpsertsMembershipForCaller()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(ParentObjective(ownerId: EmployeeId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, It.IsAny<Guid>(), EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HeadUserIdInRequestIsIgnored_UpsertsMembershipForCallerNotRequestedHead()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(ParentObjective(ownerId: EmployeeId));

        var result = await handler.Handle(ValidCommand(headEmployeeId: OtherEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, It.IsAny<Guid>(), EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AssignedHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandlerWithMembership(ParentObjective(ownerId: EmployeeId), assignee: null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidCreate_EnsuresProjectsAccessGrantedForCaller()
    {
        var (handler, _, _, autoGrant) = BuildHandlerWithMembership(ParentObjective(ownerId: EmployeeId));

        await handler.Handle(ValidCommand(), CancellationToken.None);

        autoGrant.Verify(x => x.EnsureGrantedAsync(TenantId, UserId, UserId, "projects:access", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerHasNoEmployeeRecord_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var objectives = new Mock<IObjectiveRepository>();
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_HeadEmployeeIdDifferentFromCreator_CreatorStillOwner_LeaderInvitationCreated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = BuildIdentity();
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParentObjective(ownerId: EmployeeId));

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, OtherEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = OtherEmployeeId, TenantId = TenantId, UserId = OtherUserId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ParentId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);

        var result = await handler.Handle(ValidCommand(headEmployeeId: OtherEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.OwnerId);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.InvitedEmployeeId == OtherEmployeeId && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MemberInvitationsProvided_CreatesOnePendingInvitePerEntry()
    {
        var memberOne = Guid.NewGuid();
        var memberTwo = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = BuildIdentity();
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParentObjective(ownerId: EmployeeId));

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, CancellationToken _) =>
                new Employee { Id = employeeId, TenantId = TenantId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ParentId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, identity.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object, invitations.Object);

        var result = await handler.Handle(ValidCommand(memberInvitations: new List<(Guid, string)>
        {
            (memberOne, ProjectInvitationTypes.Member), (memberTwo, ProjectInvitationTypes.Member)
        }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedEmployeeId == memberOne && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i => i.InvitedEmployeeId == memberTwo && i.InviteType == ProjectInvitationTypes.Member), It.IsAny<CancellationToken>()), Times.Once);
    }
}
