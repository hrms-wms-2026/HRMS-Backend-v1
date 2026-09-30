using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Services;
using ONEVO.Application.Features.CoreHr.Employee.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.SetTaskAttributes;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.BulkTaskActions;

public static class BulkTaskActions
{
    public const string UnexpectedFailureReason = "Unexpected error while processing this task.";

    internal static IRuleBuilderOptions<T, IReadOnlyList<Guid>> ValidTaskIds<T>(
        this IRuleBuilder<T, IReadOnlyList<Guid>> rule) =>
        rule.NotEmpty()
            .Must(ids => ids.Distinct().Count() <= BulkItemRunner.MaxItems)
            .WithMessage($"A bulk action can include at most {BulkItemRunner.MaxItems} tasks.");

    internal static async Task<Result<BulkTaskActionResponse>> RunAsync(
        IReadOnlyList<Guid> taskIds,
        Func<Guid, CancellationToken, Task<Result>> sendOne,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct)
    {
        var results = await BulkItemRunner.RunAsync(taskIds, async (id, token) =>
        {
            var result = await sendOne(id, token);
            return new BulkItemOutcome(result.IsSuccess, false, result.Error);
        }, unitOfWork, logger, UnexpectedFailureReason, ct);
        return Result<BulkTaskActionResponse>.Success(BulkTaskActionResponse.From(results));
    }
}

public sealed record BulkMoveTaskStatusCommand(
    IReadOnlyList<Guid> TaskIds,
    Guid NewStatusId) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkMoveTaskStatusCommandValidator : AbstractValidator<BulkMoveTaskStatusCommand>
{
    public BulkMoveTaskStatusCommandValidator()
    {
        RuleFor(x => x.TaskIds).ValidTaskIds();
        RuleFor(x => x.NewStatusId).NotEmpty();
    }
}

public sealed class BulkMoveTaskStatusCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkMoveTaskStatusCommandHandler> logger)
    : IRequestHandler<BulkMoveTaskStatusCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(BulkMoveTaskStatusCommand command, CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds,
            (id, token) => mediator.Send(new MoveTaskStatusCommand(id, command.NewStatusId), token),
            unitOfWork, logger, ct);
}

public sealed record BulkAssignTaskCommand(
    IReadOnlyList<Guid> TaskIds,
    Guid EmployeeId) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkAssignTaskCommandValidator : AbstractValidator<BulkAssignTaskCommand>
{
    public BulkAssignTaskCommandValidator()
    {
        RuleFor(x => x.TaskIds).ValidTaskIds();
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}

public sealed class BulkAssignTaskCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkAssignTaskCommandHandler> logger)
    : IRequestHandler<BulkAssignTaskCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(BulkAssignTaskCommand command, CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds,
            (id, token) => mediator.Send(new AssignTaskCommand(id, command.EmployeeId), token),
            unitOfWork, logger, ct);
}

public sealed record BulkSetTaskPriorityCommand(
    IReadOnlyList<Guid> TaskIds,
    string Priority) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkSetTaskPriorityCommandValidator : AbstractValidator<BulkSetTaskPriorityCommand>
{
    private static readonly string[] Priorities =
    {
        WorkTaskPriorities.Low,
        WorkTaskPriorities.Medium,
        WorkTaskPriorities.High,
        WorkTaskPriorities.Critical
    };

    public BulkSetTaskPriorityCommandValidator()
    {
        RuleFor(x => x.TaskIds).ValidTaskIds();
        RuleFor(x => x.Priority)
            .Must(p => Priorities.Contains(p))
            .WithMessage("Priority must be one of: low, medium, high, critical.");
    }
}

public sealed class BulkSetTaskPriorityCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkSetTaskPriorityCommandHandler> logger)
    : IRequestHandler<BulkSetTaskPriorityCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(BulkSetTaskPriorityCommand command, CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds,
            (id, token) => mediator.Send(new SetTaskAttributesCommand(id, command.Priority, false, null), token),
            unitOfWork, logger, ct);
}

public sealed record BulkSetTaskDueDateCommand(
    IReadOnlyList<Guid> TaskIds,
    DateOnly? DueDate) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkSetTaskDueDateCommandValidator : AbstractValidator<BulkSetTaskDueDateCommand>
{
    public BulkSetTaskDueDateCommandValidator() => RuleFor(x => x.TaskIds).ValidTaskIds();
}

public sealed class BulkSetTaskDueDateCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkSetTaskDueDateCommandHandler> logger)
    : IRequestHandler<BulkSetTaskDueDateCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(BulkSetTaskDueDateCommand command, CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds,
            (id, token) => mediator.Send(new SetTaskAttributesCommand(id, null, true, command.DueDate), token),
            unitOfWork, logger, ct);
}

public sealed record BulkConvertTasksToSubtasksCommand(
    IReadOnlyList<Guid> TaskIds,
    Guid NewParentTaskId) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkConvertTasksToSubtasksCommandValidator : AbstractValidator<BulkConvertTasksToSubtasksCommand>
{
    public BulkConvertTasksToSubtasksCommandValidator()
    {
        RuleFor(x => x.TaskIds).ValidTaskIds();
        RuleFor(x => x.NewParentTaskId).NotEmpty();
    }
}

public sealed class BulkConvertTasksToSubtasksCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkConvertTasksToSubtasksCommandHandler> logger)
    : IRequestHandler<BulkConvertTasksToSubtasksCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(
        BulkConvertTasksToSubtasksCommand command,
        CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds, async (id, token) =>
        {
            var result = await mediator.Send(
                new ConvertTaskToSubtaskCommand(id, command.NewParentTaskId), token);
            return result.IsSuccess
                ? Result.Success()
                : Result.Failure(result.Error!, result.StatusCode ?? 400);
        }, unitOfWork, logger, ct);
}

public sealed record BulkDeleteTasksCommand(
    IReadOnlyList<Guid> TaskIds) : IRequest<Result<BulkTaskActionResponse>>;

public sealed class BulkDeleteTasksCommandValidator : AbstractValidator<BulkDeleteTasksCommand>
{
    public BulkDeleteTasksCommandValidator() => RuleFor(x => x.TaskIds).ValidTaskIds();
}

public sealed class BulkDeleteTasksCommandHandler(
    IMediator mediator,
    IUnitOfWork unitOfWork,
    ILogger<BulkDeleteTasksCommandHandler> logger)
    : IRequestHandler<BulkDeleteTasksCommand, Result<BulkTaskActionResponse>>
{
    public Task<Result<BulkTaskActionResponse>> Handle(BulkDeleteTasksCommand command, CancellationToken ct) =>
        BulkTaskActions.RunAsync(command.TaskIds, async (id, token) =>
        {
            // Delete goes through the approval engine: success = deleted, or sent for approval.
            var result = await mediator.Send(new DeleteTaskCommand(id), token);
            return result.IsSuccess
                ? Result.Success()
                : Result.Failure(result.Error!, result.StatusCode ?? 400);
        }, unitOfWork, logger, ct);
}
