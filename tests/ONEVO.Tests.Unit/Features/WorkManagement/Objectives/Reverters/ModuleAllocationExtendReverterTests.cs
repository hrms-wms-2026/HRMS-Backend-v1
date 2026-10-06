using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleAllocationExtendReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleAllocationExtendReverter Build() => new(_objectives.Object);

    [Fact]
    public async Task RevertAsync_SubtractsTheAppliedHours()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, AllocatedHours = 50m, IsActive = true };
        var applied = new ModuleAllocationExtendInput(10m, "more work");
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleAllocationExtend,
            PayloadJson = JsonSerializer.Serialize(applied, ModulePayloadJson.Options), AppliedPayloadJson = null,
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        module.AllocatedHours.Should().Be(40m);
        _objectives.Verify(x => x.Update(module), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_UsesAppliedPayloadWhenApproverEditedIt()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, AllocatedHours = 50m, IsActive = true };
        var requested = new ModuleAllocationExtendInput(10m, "more work");
        var applied = new ModuleAllocationExtendInput(5m, "more work");
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleAllocationExtend,
            PayloadJson = JsonSerializer.Serialize(requested, ModulePayloadJson.Options),
            AppliedPayloadJson = JsonSerializer.Serialize(applied, ModulePayloadJson.Options),
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        module.AllocatedHours.Should().Be(45m);
    }

    [Fact]
    public async Task RevertAsync_HoursDroppedBelowApplied_ReturnsConflict()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, AllocatedHours = 5m, IsActive = true };
        var applied = new ModuleAllocationExtendInput(10m, "more work");
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleAllocationExtend,
            PayloadJson = JsonSerializer.Serialize(applied, ModulePayloadJson.Options),
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
    }
}
