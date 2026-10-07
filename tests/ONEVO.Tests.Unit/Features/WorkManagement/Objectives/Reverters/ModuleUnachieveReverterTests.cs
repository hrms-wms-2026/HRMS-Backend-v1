using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleUnachieveReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IModuleWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    private ModuleUnachieveReverter Build() => new(_writes.Object, _objectives.Object);

    [Fact]
    public async Task RevertAsync_CallsApplyAchieve()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsAchieved = false };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleUnachieve };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _writes.Setup(x => x.ApplyAchieveAsync(TenantId, module, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.ApplyAchieveAsync(TenantId, module, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_AlreadyAchieved_ReturnsStale()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsAchieved = true };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleUnachieve };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }

    [Fact]
    public async Task RevertAsync_AchieveFails_ReturnsConflict()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsAchieved = false };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleUnachieve };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _writes.Setup(x => x.ApplyAchieveAsync(TenantId, module, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure("nope", 409));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
    }
}
