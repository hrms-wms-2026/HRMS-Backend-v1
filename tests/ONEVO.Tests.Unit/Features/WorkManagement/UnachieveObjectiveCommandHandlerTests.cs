using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class UnachieveObjectiveCommandHandlerTests
{
    private static readonly UnachieveObjectiveCommand Command = new(K.ModuleId);

    private static Objective Achieved()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        module.AchievedAt = DateTimeOffset.UtcNow;
        return module;
    }

    [Fact]
    public async Task Handle_CallerAtOrAbovePosition_EngineDirect_AppliesImmediatelyAndRestoresMembership()
    {
        var kit = new K(Achieved());
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.False(kit.Module.IsAchieved);
        Assert.Null(kit.Module.AchievedAt);
        kit.Membership.Verify(x => x.UpsertMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.Head, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CreatorHeadBelowPosition_EnginePending_CreatesRequest()
    {
        var kit = new K(Achieved());
        kit.EnginePending();

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        Assert.True(kit.Module.IsAchieved);
    }

    [Fact]
    public async Task Handle_InactiveHead_ReturnsBadRequest_BeforeTheEngine()
    {
        var kit = new K(Achieved());
        kit.Membership.Setup(x => x.GetActiveAssigneeAsync(K.TenantId, K.Head, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.Equal("The current head must be an active employee in this tenant.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_NotAchieved_ReturnsConflict()
    {
        var kit = new K();

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Objective is not achieved.", result.Error);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var kit = new K(Achieved()) { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_SubmitsToTheEngine()
    {
        var kit = new K(Achieved());
        kit.CallAs(K.OtherUser);

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.Unachieve().Handle(Command, CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
