using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;

public class ApproveTaskCreationRequestCommandHandler : IRequestHandler<ApproveTaskCreationRequestCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskCreationRequestRepository _requests;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ISprintRepository _sprints;
    private readonly ITaskCategoryRepository _categories;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarEventRepository _calendarEvents;

    public ApproveTaskCreationRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskCreationRequestRepository requests,
        IObjectiveRepository objectives, IProjectRepository projects, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        ITaskCategoryRepository categories, IObjectiveAllocationSlackCalculator slack, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork, ISprintRepository sprints,
        ICalendarEventRepository calendarEvents)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _statuses = statuses;
        _sprints = sprints;
        _categories = categories;
        _slack = slack;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _calendarEvents = calendarEvents;
    }

    public async Task<Result<WorkTaskResponse>> Handle(ApproveTaskCreationRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result<WorkTaskResponse>.NotFound("Request not found.");

        if (pending.Status != TaskCreationRequestStatuses.Pending)
            return Result<WorkTaskResponse>.Conflict("This request has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, pending.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can decide this request.");

        var payload = JsonSerializer.Deserialize<TaskCreationRequestPayload>(pending.PayloadJson)!;

        var category = await _categories.GetByIdForTenantAsync(tenantId, payload.CategoryId, ct);
        if (category is null || category.ProjectId != objective.ProjectId)
            return Result<WorkTaskResponse>.NotFound("Category not found.");

        // D-B: a task materialised into a module covered by a whole-module event must fall in-window.
        var eventWindows = await _calendarEvents.ListActiveEventWindowsForObjectiveAsync(tenantId, objective.Id, ct);
        if (eventWindows.Count > 0)
        {
            if (payload.DueDate is null)
                return Result<WorkTaskResponse>.Conflict(
                    $"This module is in active event(s) {string.Join(", ", eventWindows.Select(w => w.Name))}; a due date is required.");
            var bad = eventWindows.Where(w => payload.DueDate < w.StartDate || payload.DueDate > w.EndDate).ToList();
            if (bad.Count > 0)
                return Result<WorkTaskResponse>.Conflict(
                    $"Due date {payload.DueDate:yyyy-MM-dd} is outside event window(s): " +
                    $"{string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.");
        }

        if (payload.SprintId is not null)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, payload.SprintId.Value, ct);
            if (sprint is null || sprint.ObjectiveId != objective.Id)
                return Result<WorkTaskResponse>.NotFound("Sprint not found.");
            if (sprint.Status == SprintStatuses.Achieved)
                return Result<WorkTaskResponse>.Conflict("This sprint has been achieved and is frozen.");
        }

        if (payload.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, ct: ct);
            if (payload.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, objective.ProjectId, ct);
        var defaultStatus = statuses.Where(s => !s.MarksTaskComplete).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        var project = await _projects.GetByIdForTenantAsync(tenantId, objective.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Project not found.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, objective.ProjectId, innerCt);
            var now = DateTimeOffset.UtcNow;
            var task = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                ShortId = $"{project.Identifier}-{taskNumber}",
                Title = payload.Title, Description = payload.Description, CategoryId = payload.CategoryId,
                Priority = payload.Priority, DueDate = payload.DueDate, EstimatedHours = payload.EstimatedHours,
                StoryPoints = payload.StoryPoints, StatusId = defaultStatus.Id, CompletedHours = 0m,
                SprintId = payload.SprintId,
                ProgressPercent = 0, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _tasks.AddAsync(task, innerCt);

            pending.Status = TaskCreationRequestStatuses.Approved;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecidedAt = now;
            pending.CreatedTaskId = task.Id;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_task_creation_request_decided",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "approved",
                        ["taskTitle"] = payload.Title,
                        ["objectiveName"] = objective.Title
                    },
                    "task_creation_request", pending.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId));
        }, ct);
    }
}
