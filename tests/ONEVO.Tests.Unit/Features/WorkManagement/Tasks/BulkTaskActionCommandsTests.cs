using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.BulkTaskActions;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.SetTaskAttributes;
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

    [Fact]
    public async Task BulkPriority_And_BulkDueDate_UseSetTaskAttributes()
    {
        _mediator.Setup(m => m.Send(It.IsAny<SetTaskAttributesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await new BulkSetTaskPriorityCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkSetTaskPriorityCommandHandler>.Instance)
            .Handle(new BulkSetTaskPriorityCommand(new[] { _a }, "high"), CancellationToken.None);
        await new BulkSetTaskDueDateCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkSetTaskDueDateCommandHandler>.Instance)
            .Handle(new BulkSetTaskDueDateCommand(new[] { _b }, null), CancellationToken.None);

        _mediator.Verify(m => m.Send(It.Is<SetTaskAttributesCommand>(c =>
            c.TaskId == _a && c.Priority == "high" && !c.SetDueDate), It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(It.Is<SetTaskAttributesCommand>(c =>
            c.TaskId == _b && c.Priority == null && c.SetDueDate && c.DueDate == null), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task BulkDelete_DispatchesPerTask()
    {
        Returns<DeleteTaskCommand>(_a, Result.Success());
        Returns<DeleteTaskCommand>(_b, Result.Conflict("This task has subtasks. Delete or move its subtasks first."));

        var result = await new BulkDeleteTasksCommandHandler(
                _mediator.Object, _uow, NullLogger<BulkDeleteTasksCommandHandler>.Instance)
            .Handle(new BulkDeleteTasksCommand(new[] { _a, _b }), CancellationToken.None);

        Assert.Equal(new[] { "succeeded", "failed" }, result.Value!.Items.Select(i => i.Outcome));
        Assert.Equal(2, _uow.ClearTrackingCallCount);
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
