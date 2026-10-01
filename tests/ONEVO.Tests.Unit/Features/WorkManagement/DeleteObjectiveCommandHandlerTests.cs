using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteObjectiveCommandHandlerTests
{
    private static readonly DeleteObjectiveCommand Command = new(K.ModuleId);

    [Fact]
    public async Task Handle_CallerAtOrAbovePosition_EngineDirect_AppliesImmediately()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.False(kit.Module.IsActive);
        kit.Objectives.Verify(x => x.Update(kit.Module), Times.Once);
    }

    [Fact]
    public async Task Handle_CreatorHeadBelowPosition_EnginePending_CreatesRequest()
    {
        var kit = new K(K.NewModule(createdByUser: K.HeadUser));
        kit.EnginePending();

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.False(result.Value!.Applied);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        Assert.True(kit.Module.IsActive);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var kit = new K();
        kit.EngineConflict();

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_SubmitsToTheEngine()
    {
        var kit = new K();
        kit.CallAs(K.OtherUser);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsDefault = true;
        var kit = new K(module);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Use the Project delete endpoint for the Default Objective.", result.Error);
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsConflict()
    {
        var module = K.NewModule();
        module.IsActive = false;
        var kit = new K(module);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Objective already deleted.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.Delete().Handle(Command, CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
