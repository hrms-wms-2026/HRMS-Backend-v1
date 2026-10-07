using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleDeleteReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IModuleWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleDeleteReverter Build() => new(_writes.Object, _objectives.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheDeletedModule()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = false };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleDelete };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.Restore(module), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_ModuleAlreadyActive_ReturnsStale()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleDelete };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }

    [Fact]
    public async Task RevertAsync_ModuleNotFound_ReturnsStale()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.ModuleDelete };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
