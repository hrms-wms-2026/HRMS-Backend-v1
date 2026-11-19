using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks.Reverters;

public class TaskEditReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ITaskWriteService> _writes = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private TaskEditReverter Build() => new(_writes.Object, _objectives.Object, _tasks.Object);

    [Fact]
    public async Task RevertAsync_ReplaysApplyEditWithTheSnapshottedInput()
    {
        var taskId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var task = new WorkTask { Id = taskId, TenantId = TenantId, ObjectiveId = objectiveId, Title = "New title" };
        var objective = new Objective { Id = objectiveId, TenantId = TenantId };
        var undo = new TaskEditInput("Old title", "Old description", WorkTaskPriorities.Low, null, null, null, 10, null, null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = taskId, ActionType = WorkActionTypes.TaskEdit,
            UndoStateJson = JsonSerializer.Serialize(undo, TaskPayload.Options)
        };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, objectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        _writes.Setup(x => x.ApplyEditAsync(TenantId, It.IsAny<Guid>(), task, objective, undo, TaskEditLogSources.Reverted, request.Id, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, WorkTask, Objective, TaskEditInput, string, Guid?, CancellationToken>((_, _, t, _, i, _, _, _) => t.Title = i.Title)
            .ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        task.Title.Should().Be("Old title");
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskEdit, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }

    [Fact]
    public async Task RevertAsync_TaskNotFound_ReturnsStale()
    {
        var undo = new TaskEditInput("Old title", null, WorkTaskPriorities.Low, null, null, null, 10, null, null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.TaskEdit,
            UndoStateJson = JsonSerializer.Serialize(undo, TaskPayload.Options)
        };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.TargetId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Stale);
    }
}
