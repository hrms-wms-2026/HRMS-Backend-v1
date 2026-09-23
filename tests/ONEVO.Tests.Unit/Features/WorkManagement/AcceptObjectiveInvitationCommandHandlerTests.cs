using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AcceptObjectiveInvitationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid InvitedUserId = Guid.NewGuid();
    private static readonly Guid InvitedEmployeeId = Guid.NewGuid();
    private static readonly Guid InviterUserId = Guid.NewGuid();
    private static readonly Guid InviterEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();

    private static Objective SubObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadEmployeeId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private static Objective DefaultObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Website Revamp",
        OwnerId = HeadEmployeeId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1)
    };

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, Name = "Website Revamp", Identifier = "WEB",
        LeadId = HeadEmployeeId, IsActive = true
    };

    private static ProjectMemberInvitation Invitation(string type, string status = "pending") => new()
    {
        Id = InvitationId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        InvitedEmployeeId = InvitedEmployeeId, InviteType = type, Status = status,
        InvitedById = InviterEmployeeId
    };

    private (AcceptObjectiveInvitationCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IObjectiveRepository> Objectives, Mock<IOutboxWriter> Outbox)
        BuildHandler(ProjectMemberInvitation? invitation, Objective? objective, Guid? callerId = null, bool inviterActive = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? InvitedUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, InvitedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvitedEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [InvitedEmployeeId] = "Grace Hopper" });

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, InvitationId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, InvitedEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = InvitedEmployeeId, TenantId = TenantId, UserId = InvitedUserId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, InviterEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviterActive
                ? new Employee { Id = InviterEmployeeId, TenantId = TenantId, UserId = InviterUserId, EmploymentStatusId = EmploymentStatusIds.Active }
                : null);

        var autoGrant = new Mock<IPermissionAutoGrantService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));

        var outbox = new Mock<IOutboxWriter>();
        var handler = new AcceptObjectiveInvitationCommandHandler(
            currentUser.Object, identity.Object, invitations.Object, objectives.Object, membership.Object, autoGrant.Object,
            unitOfWork.Object, projects.Object, outbox.Object);
        return (handler, invitations, membership, objectives, outbox);
    }

    [Fact]
    public async Task Handle_AcceptMemberInvite_UpsertsMembership()
    {
        var (handler, invitations, membership, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        invitations.Verify(x => x.Update(It.Is<ProjectMemberInvitation>(i => i.Status == ProjectInvitationStatuses.Accepted)), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcceptLeaderInvite_ReassignsHead()
    {
        var (handler, _, membership, objectives, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Leader), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == InvitedEmployeeId)), Times.Once);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotInvitedEmployee_ReturnsForbidden()
    {
        var (handler, _, _, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member), SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _, _, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member, status: "accepted"), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsNotFound()
    {
        var (handler, _, _, _, outbox) = BuildHandler(null, SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var achieved = SubObjective();
        achieved.IsAchieved = true;
        var (handler, _, _, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member), achieved);

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcceptDefaultObjectiveInvite_EnqueuesAcceptedNotification()
    {
        var (handler, _, membership, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member), DefaultObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p =>
                p.RecipientUserId == InviterUserId
                && p.TemplateCode == "work_project_member_accepted"
                && p.Placeholders["accepterName"] == "Grace Hopper"
                && p.Placeholders["projectName"] == "Website Revamp"
                && p.RelatedEntityType == "project_member_invitation"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AcceptNonDefaultObjectiveInvite_DoesNotEnqueueNotification()
    {
        var (handler, _, membership, _, outbox) = BuildHandler(Invitation(ProjectInvitationTypes.Member), SubObjective());

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcceptDefaultObjectiveInvite_InviterInactive_SucceedsWithoutNotification()
    {
        var (handler, _, membership, _, outbox) = BuildHandler(
            Invitation(ProjectInvitationTypes.Member), DefaultObjective(), inviterActive: false);

        var result = await handler.Handle(new AcceptObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, InvitedEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
