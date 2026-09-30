using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.BulkTaskActions;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class BulkTaskActionCommandsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();

    private void Returns<TCommand>(Guid id, Result result) where TCommand : IRequest<Result> =>
        _mediator.Setup(m => m.Send(
                It.Is<TCommand>(c => (Guid)c.GetType().GetProperty("TaskId")!.GetValue(c)! == id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task BulkStatus_DispatchesPerTask_AndMapsFailures()
    {
        var statusId = Guid.NewGuid();
        Returns<MoveTaskStatusCommand>(_a, Result.Success());
        Returns<MoveTaskStatusCommand>(_b, Result.Forbidden("Members cannot move tasks into a private status."));

        var result = await new BulkMoveTaskStatusCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkMoveTaskStatusCommandHandler>.Instance)
            .Handle(new BulkMoveTaskStatusCommand(new[] { _a, _b }, statusId), CancellationToken.None);

        Assert.Equal((1, 1), (result.Value!.Succeeded, result.Value.Failed));
        Assert.Equal("Members cannot move tasks into a private status.", result.Value.Items[1].Reason);
        _mediator.Verify(m => m.Send(
            It.Is<MoveTaskStatusCommand>(c => c.TaskId == _a && c.NewStatusId == statusId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkAssign_ReportsAlreadyAssignedAsFailedWithReason()
    {
        var employeeId = Guid.NewGuid();
        Returns<AssignTaskCommand>(_a, Result.Conflict("This employee is already assigned to the task."));

        var result = await new BulkAssignTaskCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkAssignTaskCommandHandler>.Instance)
            .Handle(new BulkAssignTaskCommand(new[] { _a }, employeeId), CancellationToken.None);

        Assert.Equal("failed", result.Value!.Items[0].Outcome);
        _mediator.Verify(m => m.Send(
            It.Is<AssignTaskCommand>(c => c.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private WorkTask StoredTask(Guid id, string priority, DateOnly? dueDate)
    {
        var task = new WorkTask
        {
            Id = id, TenantId = TenantId, Title = "Keep title", Description = "<p>keep</p>", Priority = priority,
            DueDate = dueDate, EstimatedHours = 6m, StoryPoints = 3, SprintId = Guid.NewGuid()
        };
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        return task;
    }

    private void EditReturns(Guid id, Result<TaskWriteOutcome> result) =>
        _mediator.Setup(m => m.Send(It.Is<EditTaskCommand>(c => c.TaskId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task BulkPriority_GoesThroughTaskEdit_KeepingEveryOtherField_AndCountsApprovals()
    {
        var due = new DateOnly(2026, 10, 20);
        StoredTask(_a, "low", due);
        StoredTask(_b, "low", null);
        EditReturns(_a, Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, null)));            // owner: applied
        EditReturns(_b, Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, Guid.NewGuid())));  // member: sent for approval

        var result = await new BulkSetTaskPriorityCommandHandler(
                _mediator.Object, _currentUser.Object, _tasks.Object, _uow, NullLogger<BulkSetTaskPriorityCommandHandler>.Instance)
            .Handle(new BulkSetTaskPriorityCommand(new[] { _a, _b }, "high"), CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Value!.Succeeded, result.Value.PendingApproval, result.Value.Failed));
        // Only priority changes; attachments and sprint are left alone (null = unchanged).
        _mediator.Verify(m => m.Send(It.Is<EditTaskCommand>(c =>
            c.TaskId == _a && c.Priority == "high" && c.DueDate == due && c.Title == "Keep title"
            && c.Description == "<p>keep</p>" && c.EstimatedHours == 6m && c.StoryPoints == 3
            && c.AttachmentFileIds == null && c.SprintId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDueDate_GoesThroughTaskEdit_AndReportsRefusals()
    {
        StoredTask(_a, "medium", new DateOnly(2026, 10, 1));
        EditReturns(_a, Result<TaskWriteOutcome>.Conflict("Due date is outside event window(s)."));

        var result = await new BulkSetTaskDueDateCommandHandler(
                _mediator.Object, _currentUser.Object, _tasks.Object, _uow, NullLogger<BulkSetTaskDueDateCommandHandler>.Instance)
            .Handle(new BulkSetTaskDueDateCommand(new[] { _a }, null), CancellationToken.None);

        Assert.Equal("failed", result.Value!.Items[0].Outcome);
        Assert.Equal("Due date is outside event window(s).", result.Value.Items[0].Reason);
        _mediator.Verify(m => m.Send(It.Is<EditTaskCommand>(c =>
            c.TaskId == _a && c.DueDate == null && c.Priority == "medium"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkPriority_SkipsTasksThatAlreadyHaveTheValue_WithoutFilingAnEdit()
    {
        StoredTask(_a, "high", null);

        var result = await new BulkSetTaskPriorityCommandHandler(
                _mediator.Object, _currentUser.Object, _tasks.Object, _uow, NullLogger<BulkSetTaskPriorityCommandHandler>.Instance)
            .Handle(new BulkSetTaskPriorityCommand(new[] { _a }, "high"), CancellationToken.None);

        Assert.Equal("succeeded", result.Value!.Items[0].Outcome);
        _mediator.Verify(m => m.Send(It.IsAny<EditTaskCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BulkConvert_DispatchesWithSharedParent()
    {
        var parent = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ConvertTaskToSubtaskCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkTaskResponse>.Conflict("A subtask cannot itself have subtasks."));

        var result = await new BulkConvertTasksToSubtasksCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkConvertTasksToSubtasksCommandHandler>.Instance)
            .Handle(new BulkConvertTasksToSubtasksCommand(new[] { _a }, parent), CancellationToken.None);

        Assert.Equal("A subtask cannot itself have subtasks.", result.Value!.Items[0].Reason);
        _mediator.Verify(m => m.Send(It.Is<ConvertTaskToSubtaskCommand>(c =>
            c.TaskId == _a && c.NewParentTaskId == parent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_DispatchesPerTask_AndReportsApprovalsSeparately()
    {
        var c = Guid.NewGuid();
        // Delete goes through the approval engine: deleted now, sent for approval (request id), or refused.
        _mediator.Setup(m => m.Send(It.Is<DeleteTaskCommand>(x => x.TaskId == _a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, null)));
        _mediator.Setup(m => m.Send(It.Is<DeleteTaskCommand>(x => x.TaskId == _b), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, Guid.NewGuid())));
        _mediator.Setup(m => m.Send(It.Is<DeleteTaskCommand>(x => x.TaskId == c), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskWriteOutcome>.Conflict("This task has subtasks. Delete or move its subtasks first."));

        var result = await new BulkDeleteTasksCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkDeleteTasksCommandHandler>.Instance)
            .Handle(new BulkDeleteTasksCommand(new[] { _a, _b, c }), CancellationToken.None);

        Assert.Equal(new[] { "succeeded", "pendingApproval", "failed" }, result.Value!.Items.Select(i => i.Outcome));
        Assert.Equal((1, 1, 1), (result.Value.Succeeded, result.Value.PendingApproval, result.Value.Failed));
        Assert.Equal(3, _uow.ClearTrackingCallCount);
    }

    [Fact]
    public void Validators_EnforceNonEmptyAndLimit()
    {
        var over = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();
        Assert.False(new BulkDeleteTasksCommandValidator()
            .Validate(new BulkDeleteTasksCommand(Array.Empty<Guid>())).IsValid);
        Assert.False(new BulkDeleteTasksCommandValidator()
            .Validate(new BulkDeleteTasksCommand(over)).IsValid);
        Assert.False(new BulkSetTaskPriorityCommandValidator()
            .Validate(new BulkSetTaskPriorityCommand(new[] { _a }, "urgent")).IsValid);
        Assert.False(new BulkMoveTaskStatusCommandValidator()
            .Validate(new BulkMoveTaskStatusCommand(new[] { _a }, Guid.Empty)).IsValid);
        Assert.False(new BulkAssignTaskCommandValidator()
            .Validate(new BulkAssignTaskCommand(new[] { _a }, Guid.Empty)).IsValid);
        Assert.False(new BulkConvertTasksToSubtasksCommandValidator()
            .Validate(new BulkConvertTasksToSubtasksCommand(new[] { _a }, Guid.Empty)).IsValid);
        Assert.True(new BulkSetTaskDueDateCommandValidator()
            .Validate(new BulkSetTaskDueDateCommand(new[] { _a }, null)).IsValid);
    }
}
