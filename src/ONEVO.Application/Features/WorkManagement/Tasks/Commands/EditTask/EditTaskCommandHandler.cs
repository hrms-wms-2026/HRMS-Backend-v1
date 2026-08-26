using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public class EditTaskCommandHandler : IRequestHandler<EditTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly ISprintRepository _sprints;
        private readonly IUnitOfWork _unitOfWork;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;

    public EditTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, IObjectiveRepository objectives,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork, ISprintRepository sprints,
        ICallerIdentityResolver identity, ITaskEditLogRepository editLogs, ITaskPercentageLogRepository percentageLogs)

    {
        _currentUser = currentUser;
        _tasks = tasks;
        _objectives = objectives;
        _slack = slack;
                _unitOfWork = unitOfWork;
        _sprints = sprints;
        _identity = identity;
        _editLogs = editLogs;
        _percentageLogs = percentageLogs;

    }

    public async Task<Result<WorkTaskResponse>> Handle(EditTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

                var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);

        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result<WorkTaskResponse>.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

        if (request.EstimatedHours.HasValue && request.EstimatedHours.Value != task.EstimatedHours)
        {
            var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
            if (objective is null)
                return Result<WorkTaskResponse>.NotFound("Objective not found.");

            var slack = await _slack.CalculateAsync(tenantId, objective, excludingTaskId: task.Id, ct: ct);
            if (request.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.Title = request.Title.Trim();
            task.Description = request.Description?.Trim();
            task.Priority = request.Priority;
            task.DueDate = request.DueDate;
            task.EstimatedHours = request.EstimatedHours;
            task.StoryPoints = request.StoryPoints;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId));
        }, ct);
    }
}
