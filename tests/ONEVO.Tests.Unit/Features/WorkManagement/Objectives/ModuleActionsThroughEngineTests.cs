using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RequestAllocationExtension;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public class ModuleActionsThroughEngineTests
{
    private static EditObjectiveCommand EditCommand(decimal hours = 20m)
        => new(K.ModuleId, "Renamed", "desc", new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1), hours);

    [Fact]
    public async Task Edit_ByChildHead_GoesPending_NoFieldChange()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.Edit().Handle(EditCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Applied.Should().BeFalse();
        result.Value.ApprovalRequestId.Should().Be(K.RequestId);
        kit.Module.Title.Should().Be("Module");
        kit.Submitted.Single().PayloadJson.Should().Contain("\"title\":\"Renamed\"").And.Contain("\"allocatedHours\":20");
        kit.Notified.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_ByParentOwner_AppliesDirect_NotifiesHeadAndParent()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Edit().Handle(EditCommand(), CancellationToken.None);

        result.Value!.Applied.Should().BeTrue();
        result.Value.Objective!.Title.Should().Be("Renamed");
        kit.Module.AllocatedHours.Should().Be(20m);
        var e = kit.Notified.Single();
        e.Kind.Should().Be(WorkNotificationKinds.Direct);
        e.ActionType.Should().Be(WorkActionTypes.ModuleEdit);
        e.ActorEmployeeId.Should().Be(K.ParentOwner);
        e.RecipientEmployeeIds.Should().BeEquivalentTo(new[] { K.Head, K.ParentOwner });
    }

    [Fact]
    public async Task Edit_ValidationFails_EngineNeverCalled()
    {
        var kit = new K();

        var result = await kit.Edit().Handle(EditCommand(hours: 500m), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Delete_ByCreatorWhoIsNotAtOrAbovePosition_NowGoesPending()
    {
        // The head created this Module but is not the parent's owner: the old "creator applies
        // directly" bypass is gone - the engine decides, and here it says Pending.
        var kit = new K(K.NewModule(createdByUser: K.HeadUser));
        kit.EnginePending();

        var result = await kit.Delete().Handle(new DeleteObjectiveCommand(K.ModuleId), CancellationToken.None);

        result.Value!.Applied.Should().BeFalse();
        result.Value.ApprovalRequestId.Should().Be(K.RequestId);
        kit.Module.IsActive.Should().BeTrue();
        kit.Submitted.Single().ActionType.Should().Be(WorkActionTypes.ModuleDelete);
    }

    [Fact]
    public async Task Transfer_NoReportingManager_StillCreatesLeaderInvitation_EngineNeverCalled()
    {
        var module = K.NewModule();
        module.ReportingManagerId = null;
        var kit = new K(module);

        var result = await kit.Transfer().Handle(new TransferObjectiveHeadCommand(K.ModuleId, K.NewHead), CancellationToken.None);

        result.Value!.Applied.Should().BeFalse();
        result.Value.PendingInvitation.Should().NotBeNull();
        result.Value.ApprovalRequestId.Should().BeNull();
        kit.Invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.InvitedEmployeeId == K.NewHead && i.InviteType == ProjectInvitationTypes.Leader), It.IsAny<CancellationToken>()), Times.Once);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Transfer_ByParentOwner_Direct_SwapsOwner()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Transfer().Handle(new TransferObjectiveHeadCommand(K.ModuleId, K.NewHead), CancellationToken.None);

        result.Value!.Applied.Should().BeTrue();
        kit.Module.OwnerId.Should().Be(K.NewHead);
        kit.Notified.Single().RecipientEmployeeIds.Should().BeEquivalentTo(new[] { K.Head, K.ParentOwner, K.NewHead });
    }

    [Fact]
    public async Task Achieve_Pending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.Achieve().Handle(new AchieveObjectiveCommand(K.ModuleId), CancellationToken.None);

        result.Value!.Applied.Should().BeFalse();
        result.Value.ApprovalRequestId.Should().Be(K.RequestId);
        kit.Module.IsAchieved.Should().BeFalse();
    }

    [Fact]
    public async Task AllocationExtend_ByHead_GoesPendingToParent()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.AllocationExtend().Handle(new RequestAllocationExtensionCommand(K.ModuleId, 5m, " scope grew "), CancellationToken.None);

        result.Value!.Applied.Should().BeFalse();
        var action = kit.Submitted.Single();
        action.ActionType.Should().Be(WorkActionTypes.ModuleAllocationExtend);
        action.PositionModuleId.Should().Be(K.ParentId);
        action.PayloadJson.Should().Be("{\"requestedAdditionalHours\":5,\"reason\":\"scope grew\"}");
        kit.Module.AllocatedHours.Should().Be(10m);
    }

    [Fact]
    public async Task EngineConflict409_PassesThrough()
    {
        var kit = new K();
        kit.EngineConflict();

        var result = await kit.Achieve().Handle(new AchieveObjectiveCommand(K.ModuleId), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("A request for this change is already waiting for approval.");
    }

    [Fact]
    public async Task PositionModuleIdIsCreatorPosition_TargetModuleIsSelf()
    {
        var stamped = K.NewModule();
        var grandparent = Guid.NewGuid();
        stamped.CreatorPositionObjectiveId = grandparent;
        var kit = new K(stamped);

        await kit.Delete().Handle(new DeleteObjectiveCommand(K.ModuleId), CancellationToken.None);

        var action = kit.Submitted.Single();
        action.TargetType.Should().Be(WorkTargetTypes.Module);
        action.TargetId.Should().Be(K.ModuleId);
        action.TargetModuleId.Should().Be(K.ModuleId);
        action.PositionModuleId.Should().Be(grandparent);
    }

    [Fact]
    public async Task UnstampedModule_PositionFallsBackToParent()
    {
        var kit = new K();

        await kit.Delete().Handle(new DeleteObjectiveCommand(K.ModuleId), CancellationToken.None);

        kit.Submitted.Single().PositionModuleId.Should().Be(K.ParentId);
    }
}
