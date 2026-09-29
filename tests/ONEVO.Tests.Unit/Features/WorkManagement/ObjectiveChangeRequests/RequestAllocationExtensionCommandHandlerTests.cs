using Moq;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.ObjectiveChangeRequests;

public class RequestAllocationExtensionCommandHandlerTests
{
    private static RequestAllocationExtensionCommand Command() => new(K.ModuleId, 20m, "Need more hours for the new scope");

    [Fact]
    public async Task Handle_HeadBelowPosition_EnginePending_CreatesRequestForTheParent()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.AllocationExtend().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        kit.Approvals.Verify(x => x.SubmitAsync(It.Is<ONEVO.Application.Features.WorkManagement.Approvals.Services.WorkAction>(a =>
            a.ActionType == WorkActionTypes.ModuleAllocationExtend && a.PositionModuleId == K.ParentId), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(10m, kit.Module.AllocatedHours);
    }

    [Fact]
    public async Task Handle_ParentOwner_EngineDirect_AddsHours()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.AllocationExtend().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.Equal(30m, kit.Module.AllocatedHours);
    }

    [Fact]
    public async Task Handle_RootObjectiveNoParent_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.ParentObjectiveId = null;
        module.ReportingManagerId = null;
        var kit = new K(module);

        var result = await kit.AllocationExtend().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("This milestone has no Reporting Manager to route to - it is a top-level milestone. Edit the Project directly instead.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_SubmitsToTheEngine()
    {
        var kit = new K();
        kit.CallAs(K.OtherUser);

        var result = await kit.AllocationExtend().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_CallerNotEffectiveManager_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.AllocationExtend().Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only this milestone's owner can request an allocation extension.", result.Error);
        kit.VerifyEngineNeverCalled();
    }
}
