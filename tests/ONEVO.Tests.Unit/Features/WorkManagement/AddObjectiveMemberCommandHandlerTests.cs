using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddObjectiveMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(bool isActive = true, bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadEmployeeId, IsActive = isActive, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AddObjectiveMemberCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, Employee? assignee = null, Guid? callerId = null, bool explicitNullAssignee = false,
        bool alreadyActiveMember = false, ProjectMemberInvitation? existingPendingInvite = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerUserId = callerId ?? HeadUserId;
        var resolvedCallerEmployeeId = resolvedCallerUserId == OtherUserId ? OtherEmployeeId : HeadEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, HeadUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeadEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var mockAssignee = explicitNullAssignee ? null
            : assignee ?? new Employee { Id = MemberEmployeeId, TenantId = TenantId, EmploymentStatusId = EmploymentStatusIds.Active };
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(mockAssignee);
        membership.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, ObjectiveId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyActiveMember);
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective is not null && objective.OwnerId == resolvedCallerEmployeeId));

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(TenantId, ObjectiveId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPendingInvite);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddObjectiveMemberCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, membership.Object, invitations.Object, unitOfWork.Object);
        return (handler, invitations, membership);
    }

    [Fact]
    public async Task Handle_NewInvite_CreatesPendingMemberInvitationAndReturns202Shape()
    {
        var (handler, invitations, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Equal(ProjectInvitationTypes.Member, result.Value.Invitation!.InviteType);
        membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedEmployeeId == MemberEmployeeId
            && i.InviteType == ProjectInvitationTypes.Member && i.Status == ProjectInvitationStatuses.Pending
            && i.InvitedById == HeadEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_NoOpReturnsAlreadyMemberTrue()
    {
        var (handler, invitations, _) = BuildHandler(SubObjective(), alreadyActiveMember: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyPendingInvite_ReturnsConflict()
    {
        var existing = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, InvitedEmployeeId = MemberEmployeeId,
            InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Pending
        };
        var (handler, _, _) = BuildHandler(SubObjective(), existingPendingInvite: existing);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_CreatesPendingInvite()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, invitations, _) = BuildHandler(SubObjective(), callerId: OtherUserId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyMember);
        invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == ObjectiveId && i.InvitedEmployeeId == MemberEmployeeId
            && i.InvitedById == OtherEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(), explicitNullAssignee: true);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
