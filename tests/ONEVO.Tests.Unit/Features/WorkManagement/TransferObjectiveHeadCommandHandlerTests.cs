using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class TransferObjectiveHeadCommandHandlerTests
{
    private static TransferObjectiveHeadCommand ValidCommand() => new(K.ModuleId, K.NewHead);

    private static K ParentOwnerKit()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);
        return kit;
    }

    [Fact]
    public async Task Handle_CallerAtOrAbovePosition_UpsertsNewHeadMembershipAndDeactivatesOld()
    {
        var kit = ParentOwnerKit();

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.Equal(K.NewHead, kit.Module.OwnerId);
        kit.Membership.Verify(x => x.UpsertMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.NewHead, It.IsAny<CancellationToken>()), Times.Once);
        kit.Membership.Verify(x => x.DeactivateMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.Head, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OldHeadHasNoOtherAccess_DropsThemFromProject()
    {
        var kit = ParentOwnerKit();

        await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        kit.Membership.Verify(x => x.HasOtherActiveAccessAsync(K.TenantId, K.ProjectId, K.Head, K.ModuleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerAtOrAbovePosition_CascadesReportingManagerToDirectChildren()
    {
        var kit = ParentOwnerKit();
        var child = new Objective { Id = Guid.NewGuid(), ReportingManagerId = K.Head, IsActive = true };
        kit.Objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { child });

        await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(K.NewHead, child.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CreatorHeadBelowPosition_EnginePending_CreatesRequest_DoesNotTouchMembershipYet()
    {
        var kit = new K(K.NewModule(createdByUser: K.HeadUser));
        kit.EnginePending();

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        Assert.Null(result.Value.PendingInvitation);
        Assert.Equal(K.Head, kit.Module.OwnerId);
        Assert.Equal("{\"newHeadEmployeeId\":\"" + K.NewHead + "\"}", kit.Submitted.Single().PayloadJson);
        kit.Membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var kit = new K();

        var result = await kit.Transfer().Handle(new TransferObjectiveHeadCommand(K.ModuleId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("The new head must be an active employee in this tenant.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_SubmitsToTheEngine()
    {
        var kit = new K();
        kit.CallAs(K.OtherUser);

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var kit = new K();
        kit.EngineConflict();

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsDefault = true;
        var kit = new K(module);

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("The Default Objective's head cannot be transferred.", result.Error);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var module = K.NewModule();
        module.IsActive = false;
        var kit = new K(module);

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveHasNoReportingManager_CreatesLeaderInvitationInsteadOfARequest()
    {
        var module = K.NewModule(createdByUser: K.OtherUser);
        module.ReportingManagerId = null;
        var kit = new K(module);

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingInvitation);
        Assert.Null(result.Value.ApprovalRequestId);
        Assert.Equal(K.Head, module.OwnerId);
        kit.Invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ObjectiveId == K.ModuleId && i.InvitedEmployeeId == K.NewHead
            && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
        kit.Outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p =>
                p.TemplateCode == "work_objective_invitation_created"
                && p.RelatedEntityType == "project_member_invitation"
                && p.RecipientUserId == K.NewHeadUser
                && p.Placeholders["inviteType"] == ProjectInvitationTypes.Leader),
            K.TenantId, It.IsAny<CancellationToken>()), Times.Once);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_NoReportingManager_LeaderInvitationAlreadyPending_ReturnsConflict()
    {
        var module = K.NewModule();
        module.ReportingManagerId = null;
        var kit = new K(module);
        kit.Invitations.Setup(x => x.ListPendingForObjectiveAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation> { new() { InviteType = ProjectInvitationTypes.Leader } });

        var result = await kit.Transfer().Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }
}
