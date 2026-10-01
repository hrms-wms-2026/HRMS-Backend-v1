using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditObjectiveCommandHandlerTests
{
    private static EditObjectiveCommand Command(decimal hours = 20m)
        => new(K.ModuleId, "  Renamed  ", "desc", new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1), hours);

    [Fact]
    public async Task Handle_EditByHeadBelowPosition_EnginePending_ReturnsApprovalRequestId()
    {
        var kit = new K();
        kit.EnginePending();

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Null(result.Value.Objective);
        Assert.Equal(K.RequestId, result.Value.ApprovalRequestId);
        Assert.Equal("Module", kit.Module.Title);
    }

    [Fact]
    public async Task Handle_EditByCreatorHead_IsStillDecidedByThePositionRule_NotACreatorBypass()
    {
        var kit = new K(K.NewModule(createdByUser: K.HeadUser));
        kit.EnginePending();

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.False(result.Value!.Applied);
        kit.Approvals.Verify(x => x.SubmitAsync(It.Is<ONEVO.Application.Features.WorkManagement.Approvals.Services.WorkAction>(a =>
            a.ActionType == WorkActionTypes.ModuleEdit && a.ActorEmployeeId == K.Head && a.PositionModuleId == K.ParentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EngineDirect_AppliesTrimmedFieldsAndReturnsDetail()
    {
        var kit = new K();
        kit.CallAs(K.ParentOwnerUser);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.True(result.Value!.Applied);
        Assert.Equal("Renamed", result.Value.Objective!.Title);
        Assert.Equal(20m, kit.Module.AllocatedHours);
        Assert.Null(result.Value.ApprovalRequestId);
    }

    [Fact]
    public async Task Handle_EditExceedingParent_Returns409_BeforeTheEngine()
    {
        var kit = new K();

        var result = await kit.Edit().Handle(Command(hours: 500m), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("The edited date range or allocated hours would exceed the parent milestone's.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var kit = new K();
        kit.EngineConflict();

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var kit = new K { OtherIsMember = false };
        kit.CallAs(K.OtherUser);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only this milestone's head can edit it.", result.Error);
        kit.VerifyEngineNeverCalled();
    }

    [Fact]
    public async Task Handle_CallerIsActiveMemberOfAncestorObjective_SubmitsToTheEngine()
    {
        var kit = new K();
        kit.CallAs(K.OtherUser);
        kit.EnginePending();

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(K.Other, kit.Submitted.Single().ActorEmployeeId);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsDefault = true;
        var kit = new K(module);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Use the Project edit endpoint for the Default Objective.", result.Error);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var kit = new K();
        kit.Objectives.Setup(x => x.GetByIdForTenantAsync(K.TenantId, K.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveInactive_ReturnsNotFound()
    {
        var module = K.NewModule();
        module.IsActive = false;
        var kit = new K(module);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var module = K.NewModule();
        module.IsAchieved = true;
        var kit = new K(module);

        var result = await kit.Edit().Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("An achieved milestone cannot be edited.", result.Error);
        kit.VerifyEngineNeverCalled();
    }
}
