using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleEditReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IModuleWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleEditReverter Build() => new(_writes.Object, _objectives.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheSnapshottedFields()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, Title = "New title", IsActive = true };
        var undo = new ModuleEditInput("Old title", "Old description", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 40m);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleEdit,
            UndoStateJson = JsonSerializer.Serialize(undo, ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _writes.Setup(x => x.ApplyEditAsync(TenantId, module, undo, It.IsAny<CancellationToken>()))
            .Callback<Guid, Objective, ModuleEditInput, CancellationToken>((_, m, i, _) => m.Title = i.Title)
            .ReturnsAsync(ONEVO.Application.Common.Models.Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        module.Title.Should().Be("Old title");
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.ModuleEdit, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }
}
