using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AchieveObjectiveCommandHandlerTests
{
    private static readonly AchieveObjectiveCommand Command = new(K.ModuleId);

    [Fact]
    public async Task Handle_CallerAtOrAbovePosition_EngineDirect_AppliesImmediately()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.True(kit.Module.IsAchieved);
        kit.Membership.Verify(x => x.DeactivateMembershipAsync(K.TenantId, K.ProjectId, K.ModuleId, K.Head, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CreatorHeadBelowPosition_EnginePending_CreatesRequest()
    {
        var kit = new K(K.NewModule(createdByUser: K.HeadUser));
        kit.EnginePending();

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        Assert.False(kit.Module.IsAchieved);
        kit.Membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DirectChildNotAchieved_ReturnsBadRequest()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { new() { Id = Guid.NewGuid(), IsActive = true, IsAchieved = false } });

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("All sub-milestones must be achieved before this one can be.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_AlreadyAchieved_ReturnsConflict()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Objective is already achieved.", result.Error);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_SubmitsToTheEngine()
    {
        var kit = new K();
        kit.CallAs(K.OtherUser);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsDefault = true;
        var kit = new K(module);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Use the Project achieve endpoint for the Default Objective.", result.Error);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var kit = new K();
        kit.EngineConflict();

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ActiveSprintTasks_ReturnsFailure()
    {
        var kit = new K();
        kit.Sprints.Setup(x => x.AnyActiveContainingObjectiveTasksAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await kit.Achieve().Handle(Command, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Tasks of this milestone are still in an Active sprint - complete that sprint first.", result.Error);
        kit.VerifyEngineNeverCalled();
    }
}
