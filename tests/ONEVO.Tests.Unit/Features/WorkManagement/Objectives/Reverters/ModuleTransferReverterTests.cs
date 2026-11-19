using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleTransferReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IModuleWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleTransferReverter Build() => new(_writes.Object, _objectives.Object);

    [Fact]
    public async Task RevertAsync_TransfersBackToThePreviousHead()
    {
        var moduleId = Guid.NewGuid();
        var oldHead = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, OwnerId = Guid.NewGuid(), IsActive = true };
        var undo = new ModuleTransferInput(oldHead);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleTransfer,
            UndoStateJson = JsonSerializer.Serialize(undo, ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _writes.Setup(x => x.ApplyTransferAsync(TenantId, module, undo, It.IsAny<CancellationToken>()))
            .Callback<Guid, Objective, ModuleTransferInput, CancellationToken>((_, m, i, _) => m.OwnerId = i.NewHeadEmployeeId)
            .ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        module.OwnerId.Should().Be(oldHead);
    }

    [Fact]
    public async Task RevertAsync_AlreadyTransferredAgainToSameHead_ReturnsStale()
    {
        var moduleId = Guid.NewGuid();
        var oldHead = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, OwnerId = oldHead, IsActive = true };
        var undo = new ModuleTransferInput(oldHead);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleTransfer,
            UndoStateJson = JsonSerializer.Serialize(undo, ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.ModuleTransfer, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }
}
