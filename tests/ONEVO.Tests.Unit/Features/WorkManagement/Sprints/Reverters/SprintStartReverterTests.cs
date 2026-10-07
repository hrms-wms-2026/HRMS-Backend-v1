using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Reverters;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints.Reverters;

public class SprintStartReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ISprintRepository> _sprints = new();

    private SprintStartReverter Build() => new(_sprints.Object);

    [Fact]
    public async Task RevertAsync_RestoresDraftAndPreviousGoal()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint
        {
            Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Active,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 14), Goal = "Ship it",
        };
        var undo = new SprintStartUndoSnapshot("Old goal");
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintStart,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        sprint.Status.Should().Be(SprintStatuses.Draft);
        sprint.StartDate.Should().BeNull();
        sprint.EndDate.Should().BeNull();
        sprint.Goal.Should().Be("Old goal");
    }

    [Fact]
    public async Task RevertAsync_SnapshotHasNullPreviousGoal_ClearsGoalToNull()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Active, Goal = "Ship it" };
        var undo = new SprintStartUndoSnapshot(null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintStart,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        sprint.Status.Should().Be(SprintStatuses.Draft);
        sprint.Goal.Should().BeNull();
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshotAtAll_LeavesGoalAlone_LegacyData()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Active, Goal = "Ship it" };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintStart, UndoStateJson = null };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        sprint.Status.Should().Be(SprintStatuses.Draft);
        sprint.Goal.Should().Be("Ship it");
    }

    [Fact]
    public async Task RevertAsync_NotActive_ReturnsStale()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Status = SprintStatuses.Complete };
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintStart, UndoStateJson = null };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
