using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.CancelObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CancelObjectiveInvitationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid SenderEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();

    private static ProjectMemberInvitation Invitation(string status = "pending", DateTimeOffset? createdAt = null) => new()
    {
        Id = InvitationId, TenantId = TenantId, ProjectId = Guid.NewGuid(), ObjectiveId = ObjectiveId,
        InvitedEmployeeId = Guid.NewGuid(), InvitedById = SenderEmployeeId, InviteType = ProjectInvitationTypes.Member,
        Status = status, CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    private (CancelObjectiveInvitationCommandHandler Handler, Mock<IProjectMemberInvitationRepository> Invitations) BuildHandler(
        ProjectMemberInvitation? invitation, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? SenderUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, SenderUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SenderEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        invitations.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, InvitationId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CancelObjectiveInvitationCommandHandler(currentUser.Object, identity.Object, invitations.Object, unitOfWork.Object);
        return (handler, invitations);
    }

    [Fact]
    public async Task Handle_SenderCancelsPendingInvite_MarksCancelled()
    {
        var (handler, invitations) = BuildHandler(Invitation());

        var result = await handler.Handle(new CancelObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        invitations.Verify(x => x.Update(It.Is<ProjectMemberInvitation>(i => i.Status == ProjectInvitationStatuses.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotTheSender_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Invitation(), callerId: OtherUserId);

        var result = await handler.Handle(new CancelObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvitationAlreadyDecided_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(Invitation(status: "accepted"));

        var result = await handler.Handle(new CancelObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new CancelObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WindowExpired_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(Invitation(createdAt: DateTimeOffset.UtcNow.AddMinutes(-(ApprovalRevertWindow.Minutes + 1))));

        var result = await handler.Handle(new CancelObjectiveInvitationCommand(InvitationId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
