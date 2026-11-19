using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Reverters;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints.Reverters;

public class SprintDeleteReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ISprintWriteService> _writes = new();
    private readonly Mock<ISprintRepository> _sprints = new();

    private SprintDeleteReverter Build() => new(_writes.Object, _sprints.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheDeletedSprintAndItsTasks()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, IsDeleted = true };
        var taskId = Guid.NewGuid();
        var undo = new SprintDeleteUndoSnapshot(new[] { taskId });
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintDelete,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantIncludingDeletedAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _writes.Setup(x => x.Restore(TenantId, sprint, undo.TaskIds, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.Restore(TenantId, sprint, undo.TaskIds, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.SprintDelete, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }

    [Fact]
    public async Task RevertAsync_RestoreFails_ReturnsConflict()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, IsDeleted = true };
        var taskId = Guid.NewGuid();
        var undo = new SprintDeleteUndoSnapshot(new[] { taskId });
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintDelete,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantIncludingDeletedAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _writes.Setup(x => x.Restore(TenantId, sprint, undo.TaskIds, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Conflict("moved elsewhere"));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
    }

    [Fact]
    public async Task RevertAsync_SprintNotDeleted_ReturnsStale()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, IsDeleted = false };
        var undo = new SprintDeleteUndoSnapshot(Array.Empty<Guid>());
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintDelete,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantIncludingDeletedAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
