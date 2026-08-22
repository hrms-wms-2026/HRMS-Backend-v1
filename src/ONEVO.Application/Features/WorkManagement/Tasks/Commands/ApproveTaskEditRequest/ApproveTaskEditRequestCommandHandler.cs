using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskEditRequest;

public class ApproveTaskEditRequestCommandHandler
    : IRequestHandler<ApproveTaskEditRequestCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveTaskEditRequestCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ITaskEditRequestRepository requests,
        IWorkTaskRepository tasks,
        IObjectiveRepository objectives,
        ISprintRepository sprints,
        IObjectiveAllocationSlackCalculator slack,
        IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _tasks = tasks;
        _objectives = objectives;
        _sprints = sprints;
        _slack = slack;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(
        ApproveTaskEditRequestCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result<WorkTaskResponse>.NotFound("Request not found.");

        if (pending.Status != TaskEditRequestStatuses.Pending)
            return Result<WorkTaskResponse>.Conflict("This request has already been decided.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, pending.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden(
                "Only this milestone's owner can decide this request.");

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result<WorkTaskResponse>.Conflict(
                    "This task's sprint has been achieved and is now frozen.");
        }

        var payload = JsonSerializer.Deserialize<TaskEditRequestPayload>(pending.PayloadJson)!;

        if (payload.EstimatedHours.HasValue && payload.EstimatedHours.Value != task.EstimatedHours)
        {
            var availableSlack = await _slack.CalculateAsync(
                tenantId, objective, excludingTaskId: task.Id, ct: ct);
            if (payload.EstimatedHours.Value > availableSlack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(
                        new InsufficientAllocationResponse(availableSlack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            task.Title = payload.Title;
            task.Description = payload.Description;
            task.Priority = payload.Priority;
            task.DueDate = payload.DueDate;
            task.EstimatedHours = payload.EstimatedHours;
            task.StoryPoints = payload.StoryPoints;
            task.UpdatedAt = now;

            pending.Status = TaskEditRequestStatuses.Approved;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(
                tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId,
                    requester.UserId,
                    "work_task_edit_request_decided",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "approved",
                        ["taskTitle"] = task.Title,
                        ["objectiveName"] = objective.Title
                    },
                    "task_edit_request",
                    pending.Id,
                    innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id,
                task.ObjectiveId,
                task.ShortId,
                task.Title,
                task.Description,
                task.TaskType,
                task.StatusId,
                task.Priority,
                task.StoryPoints,
                task.DueDate,
                task.EstimatedHours,
                task.CompletedHours,
                task.ProgressPercent,
                task.SprintId));
        }, ct);
    }
}
