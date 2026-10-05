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

public class ModuleMemberRemoveReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();

    private ModuleMemberRemoveReverter Build() => new(_objectives.Object, _membership.Object);

    [Fact]
    public async Task RevertAsync_ReAddsTheRemovedMember()
    {
        var moduleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleMemberRemove,
            PayloadJson = JsonSerializer.Serialize(new ModuleMemberRemoveInput(employeeId), ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _membership.Setup(x => x.ApplyMemberAddAsync(TenantId, module, It.IsAny<Guid>(), employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation>.Success(null!));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _membership.Verify(x => x.ApplyMemberAddAsync(TenantId, module, It.IsAny<Guid>(), employeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_ModuleGone_ReturnsStale()
    {
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.ModuleMemberRemove,
            PayloadJson = JsonSerializer.Serialize(new ModuleMemberRemoveInput(Guid.NewGuid()), ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((Objective?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }

    [Fact]
    public async Task RevertAsync_ReAddFails_ReturnsConflict()
    {
        var moduleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var module = new Objective { Id = moduleId, TenantId = TenantId, IsActive = true };
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = moduleId, ActionType = WorkActionTypes.ModuleMemberRemove,
            PayloadJson = JsonSerializer.Serialize(new ModuleMemberRemoveInput(employeeId), ModulePayloadJson.Options)
        };
        _objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, moduleId, It.IsAny<CancellationToken>())).ReturnsAsync(module);
        _membership.Setup(x => x.ApplyMemberAddAsync(TenantId, module, It.IsAny<Guid>(), employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation>.Conflict("already a member"));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
    }
}
