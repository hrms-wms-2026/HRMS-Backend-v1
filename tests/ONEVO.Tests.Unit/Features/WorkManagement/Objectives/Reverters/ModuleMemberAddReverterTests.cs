using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives.Reverters;

public class ModuleMemberAddReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<ITaskAssignmentRepository> _assignments = new();

    private ModuleMemberAddReverter Build() => new(_objectives.Object, _membership.Object, _assignments.Object);

    private WorkApprovalRequest Request(Guid moduleId, Guid employeeId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleMemberAdd,
        PayloadJson = JsonSerializer.Serialize(new ModuleMemberAddInput(employeeId, Guid.NewGuid()), ModulePayloadJson.Options)
    };

    [Fact]
    public async Task RevertAsync_MemberNotYetAssignedAnyTask_RemovesThem()
    {
        var moduleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = Request(moduleId, employeeId);
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _membership.Setup(x => x.ApplyMemberRemoveAsync(TenantId, module, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _membership.Verify(x => x.ApplyMemberRemoveAsync(TenantId, module, employeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_MemberAlreadyAssignedATask_ReturnsConflict()
    {
        var moduleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = Request(moduleId, employeeId);
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _assignments.Setup(x => x.AnyForEmployeeInObjectiveAsync(TenantId, moduleId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
        _membership.Verify(x => x.ApplyMemberRemoveAsync(It.IsAny<Guid>(), It.IsAny<Objective>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevertAsync_ModuleGone_ReturnsStale()
    {
        var request = Request(Guid.NewGuid(), Guid.NewGuid());
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
