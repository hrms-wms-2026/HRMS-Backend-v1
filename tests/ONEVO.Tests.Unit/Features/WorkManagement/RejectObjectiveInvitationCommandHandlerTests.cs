using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RejectObjectiveInvitationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InvitedUserId = Guid.NewGuid();
    private static readonly Guid InvitedEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();

    private static ProjectMemberInvitation Invitation(string status = "pending") => new()
    {
        Id = InvitationId, TenantId = TenantId, ProjectId = Guid.NewGuid(), ObjectiveId = ObjectiveId,
        InvitedEmployeeId = InvitedEmployeeId, InviteType = ProjectInvitationTypes.Leader, Status = status
    };

    private static Objective TargetObjective(bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = Guid.NewGuid(), IsDefault = false, Title = "Sub",
        OwnerId = Guid.NewGuid(), IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    private (RejectObjectiveInvitationCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations) BuildHandler(
        ProjectMemberInvitation? invitation, Objective? objective = null, Guid? callerId = null)
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

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, InvitationId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective ?? TargetObjective());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectObjectiveInvitationCommandHandler(
            currentUser.Object, identity.Object, invitations.Object, objectives.Object, unitOfWork.Object);
        return (handler, invitations);
    }

    [Fact]
    public async Task Handle_RejectPendingInvite_MarksDeclined_NoObjectiveSideEffects()
    {
        var (handler, invitations) = BuildHandler(Invitation());

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.Update(It.Is<ProjectMemberInvitation>(i => i.Status == ProjectInvitationStatuses.Declined)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotInvitedEmployee_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Invitation(), callerId: OtherUserId);

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(Invitation(status: "accepted"));

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(Invitation(), objective: TargetObjective(isAchieved: true));

        var result = await handler.Handle(new RejectObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
