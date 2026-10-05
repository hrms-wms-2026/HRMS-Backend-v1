using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddObjectiveMemberCommandHandlerTests
{
    private static AddObjectiveMemberCommand Command() => new(K.ModuleId, K.NewHead);

    [Fact]
    public async Task Handle_AddByHeadBelowPosition_EnginePending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.False(result.Value.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
    }

    [Fact]
    public async Task Handle_EngineDirect_CreatesInvitationAndReturnsIt()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.False(result.Value.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Null(result.Value.ApprovalRequestId);
        kit.Membership.Verify(x => x.ApplyMemberAddAsync(K.TenantId, kit.Module, K.ParentOwner, K.NewHead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_ShortCircuitsBeforeTheEngine()
    {
        var kit = new K();
        kit.Membership.Setup(x => x.HasActiveMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_AlreadyPendingInvite_ReturnsConflict_BeforeTheEngine()
    {
        var kit = new K();
        kit.Invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(K.TenantId, K.ModuleId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation
            {
                Id = Guid.NewGuid(), TenantId = K.TenantId, ObjectiveId = K.ModuleId, InvitedEmployeeId = K.NewHead
            });

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest_BeforeTheEngine()
    {
        var kit = new K();
        kit.Membership.Setup(x => x.GetActiveAssigneeAsync(K.TenantId, K.NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerNotMember_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.AddMember().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
