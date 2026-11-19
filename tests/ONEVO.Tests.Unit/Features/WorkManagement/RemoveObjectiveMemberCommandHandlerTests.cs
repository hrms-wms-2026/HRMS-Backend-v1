using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RemoveObjectiveMemberCommandHandlerTests
{
    private static RemoveObjectiveMemberCommand Command() => new(K.ModuleId, K.NewHead);

    [Fact]
    public async Task Handle_RemoveByHeadBelowPosition_EnginePending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
    }

    [Fact]
    public async Task Handle_EngineDirect_RemovesMember()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.Null(result.Value.ApprovalRequestId);
        kit.Membership.Verify(x => x.ApplyMemberRemoveAsync(K.TenantId, kit.Module, K.NewHead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetIsCurrentHead_ReturnsBadRequest_BeforeTheEngine()
    {
        var kit = new K();

        var result = await kit.RemoveMember().Handle(new RemoveObjectiveMemberCommand(K.ModuleId, K.Head), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerNotMember_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.RemoveMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
