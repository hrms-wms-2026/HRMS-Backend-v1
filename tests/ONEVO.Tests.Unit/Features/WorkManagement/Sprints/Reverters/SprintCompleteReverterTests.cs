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

public class SprintCompleteReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ISprintWriteService> _writes = new();
    private readonly Mock<ISprintRepository> _sprints = new();

    private SprintCompleteReverter Build() => new(_writes.Object, _sprints.Object);

    [Fact]
    public async Task RevertAsync_HappyPath_CallsApplyUncomplete()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Complete };
        var movedTaskId = Guid.NewGuid();
        var undo = new SprintCompleteUndoSnapshot(new[] { movedTaskId });
        var appliedInput = new SprintCompleteInput("backlog", null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintComplete,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options),
            PayloadJson = JsonSerializer.Serialize(appliedInput, SprintPayloadJson.Options),
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _writes.Setup(x => x.ApplyUncompleteAsync(TenantId, It.IsAny<Guid>(), sprint, undo.MovedTaskIds, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        _writes.Verify(x => x.ApplyUncompleteAsync(TenantId, It.IsAny<Guid>(), sprint, undo.MovedTaskIds, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.SprintComplete, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }

    [Fact]
    public async Task RevertAsync_MovedTaskHasMovedAgain_PropagatesConflict()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Complete };
        var movedTaskId = Guid.NewGuid();
        var undo = new SprintCompleteUndoSnapshot(new[] { movedTaskId });
        var appliedInput = new SprintCompleteInput("backlog", null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintComplete,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options),
            PayloadJson = JsonSerializer.Serialize(appliedInput, SprintPayloadJson.Options),
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _writes.Setup(x => x.ApplyUncompleteAsync(TenantId, It.IsAny<Guid>(), sprint, undo.MovedTaskIds, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Conflict("moved again"));

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Conflict);
    }
}
