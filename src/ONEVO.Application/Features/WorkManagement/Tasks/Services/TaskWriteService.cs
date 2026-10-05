using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskWriteService : ITaskWriteService
{
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ISprintRepository _sprints;
    private readonly ITaskCategoryRepository _categories;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly ISprintActivityLogRepository _sprintLogs;
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;

    public TaskWriteService(
        IObjectiveRepository objectives, IProjectRepository projects, IWorkTaskRepository tasks,
        ITaskStatusRepository statuses, ISprintRepository sprints, ITaskCategoryRepository categories,
        IObjectiveAllocationSlackCalculator slack, ICalendarEventRepository calendarEvents,
        ISprintActivityLogRepository sprintLogs, ITaskEditLogRepository editLogs, ITaskPercentageLogRepository percentageLogs)
    {
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _statuses = statuses;
        _sprints = sprints;
        _categories = categories;
        _slack = slack;
        _calendarEvents = calendarEvents;
        _sprintLogs = sprintLogs;
        _editLogs = editLogs;
        _percentageLogs = percentageLogs;
    }

    public async Task<Result> ValidateCreateAsync(Guid tenantId, TaskCreateInput input, CancellationToken ct = default)
    {
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, input.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, objective.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        // D-B: a task created in a module that a whole-module event covers must have a due date
        // inside every such event's window (spec §5.7).
        var eventWindows = await _calendarEvents.ListActiveEventWindowsForObjectiveAsync(tenantId, objective.Id, ct);
        if (eventWindows.Count > 0)
        {
            if (input.DueDate is null)
                return Result.Conflict(
                    $"This module is in active event(s) {string.Join(", ", eventWindows.Select(w => w.Name))}; a due date is required.");
            var bad = eventWindows.Where(w => input.DueDate < w.StartDate || input.DueDate > w.EndDate).ToList();
            if (bad.Count > 0)
                return Result.Conflict(
                    $"Due date {input.DueDate:yyyy-MM-dd} is outside event window(s): " +
                    $"{string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.");
        }

        if (await FindDefaultStatusAsync(tenantId, project.Id, ct) is null)
            return Result.Failure("No task statuses configured for this milestone yet.", 422);

        var category = await _categories.GetByIdForTenantAsync(tenantId, input.CategoryId, ct);
        if (category is null || category.ProjectId != project.Id)
            return Result.NotFound("Category not found.");

        if (input.SprintId is not null)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, input.SprintId.Value, ct);
            if (sprint is null || sprint.ProjectId != objective.ProjectId || sprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result.NotFound("Target sprint must be a Draft or Active sprint in the same project.");
        }

        if (input.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, ct: ct);
            if (input.EstimatedHours.Value > slack)
                return Result.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return Result.Success();
    }

    public async Task<Result<WorkTask>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, TaskCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default)
    {
        var validation = await ValidateCreateAsync(tenantId, input, ct);
        if (!validation.IsSuccess)
            return Result<WorkTask>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var objective = (await _objectives.GetByIdForTenantAsync(tenantId, input.ObjectiveId, ct))!;
        var project = (await _projects.GetByIdForTenantAsync(tenantId, objective.ProjectId, ct))!;
        var defaultStatus = (await FindDefaultStatusAsync(tenantId, project.Id, ct))!;

        var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, objective.ProjectId, ct);
        var now = DateTimeOffset.UtcNow;
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
            ShortId = $"{project.Identifier}-{taskNumber}",
            StatusId = defaultStatus.Id,
            Title = input.Title.Trim(), Description = input.Description?.Trim(),
            CategoryId = input.CategoryId, Priority = input.Priority, DueDate = input.DueDate,
            EstimatedHours = input.EstimatedHours, StoryPoints = input.StoryPoints,
            SprintId = input.SprintId,
            CompletedHours = 0m, ProgressPercent = 0, CreatedById = creatorUserId, CreatedAt = now,
            CreatorPositionObjectiveId = creatorPositionObjectiveId
        };

        await _tasks.AddAsync(task, ct);

        if (task.SprintId is not null)
            await _sprintLogs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, task.SprintId.Value, actorEmployeeId, SprintActivityActions.TasksAdded,
                details: new { taskIds = new[] { task.Id } }), ct);

        return Result<WorkTask>.Success(task);
    }

    public async Task<Result> ValidateEditAsync(Guid tenantId, WorkTask task, Objective objective, TaskEditInput input, CancellationToken ct = default)
    {
        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

        // Omitted (null) means "leave the current sprint assignment alone" - existing callers of this
        // endpoint (e.g. the task edit form) never send SprintId at all, so treating null as "clear the
        // sprint" here would silently kick every edited task out of its sprint. Only an explicit value
        // moves the task; there is no way to unassign back to the backlog through this field yet.
        if (input.SprintId.HasValue && input.SprintId.Value != task.SprintId)
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, input.SprintId.Value, ct);
            if (targetSprint is null || targetSprint.ProjectId != objective.ProjectId)
                return Result.Conflict("Target sprint must belong to the same project.");
            if (targetSprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result.Forbidden("Tasks can only be moved into a Draft or Active sprint.");
        }

        // R3: a member of an active event cannot have its due date moved outside that event's window.
        if (input.DueDate != task.DueDate)
        {
            var windows = await _calendarEvents.ListActiveEventWindowsForTaskAsync(tenantId, task.Id, task.ObjectiveId, ct);
            var windowError = TaskDueDateEventWindowRule.Validate(windows, input.DueDate);
            if (windowError is not null)
                return Result.Conflict(windowError);
        }

        if (input.EstimatedHours.HasValue && input.EstimatedHours.Value != task.EstimatedHours)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, excludingTaskId: task.Id, ct: ct);
            if (input.EstimatedHours.Value > slack)
                return Result.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return Result.Success();
    }

    public async Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, WorkTask trackedTask, Objective objective,
        TaskEditInput input, string editLogSource, Guid? approvalRequestId, CancellationToken ct = default)
    {
        var validation = await ValidateEditAsync(tenantId, trackedTask, objective, input, ct);
        if (!validation.IsSuccess)
            return validation;

        var task = trackedTask;
        Sprint? targetSprint = null;
        var previousSprintId = task.SprintId;
        if (input.SprintId.HasValue && input.SprintId.Value != task.SprintId)
            targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, input.SprintId.Value, ct);

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        void TrackChange(string field, object? oldValue, object? newValue)
        {
            if (Equals(oldValue, newValue)) return;
            oldValues[field] = oldValue;
            newValues[field] = newValue;
        }

        TrackChange("title", task.Title, input.Title.Trim());
        TrackChange("description", task.Description, input.Description?.Trim());
        TrackChange("priority", task.Priority, input.Priority);
        TrackChange("dueDate", task.DueDate, input.DueDate);
        TrackChange("estimatedHours", task.EstimatedHours, input.EstimatedHours);
        TrackChange("storyPoints", task.StoryPoints, input.StoryPoints);
        if (input.ProgressPercent.HasValue)
            TrackChange("progressPercent", task.ProgressPercent, input.ProgressPercent.Value);
        if (targetSprint is not null)
            TrackChange("sprintId", task.SprintId, targetSprint.Id);

        var now = DateTimeOffset.UtcNow;
        task.Title = input.Title.Trim();
        task.Description = input.Description?.Trim();
        task.Priority = input.Priority;
        task.DueDate = input.DueDate;
        task.EstimatedHours = input.EstimatedHours;
        task.StoryPoints = input.StoryPoints;
        if (targetSprint is not null)
        {
            task.SprintId = targetSprint.Id;
            await _sprintLogs.AddAsync(SprintActivityLogFactory.Create(tenantId, targetSprint.Id, actorEmployeeId,
                SprintActivityActions.TasksAdded, details: new { taskIds = new[] { task.Id } }), ct);
            if (previousSprintId is not null)
                await _sprintLogs.AddAsync(SprintActivityLogFactory.Create(tenantId, previousSprintId.Value, actorEmployeeId,
                    SprintActivityActions.TasksRemoved, details: new { taskIds = new[] { task.Id } }), ct);
        }

        if (input.ProgressPercent.HasValue && input.ProgressPercent.Value != task.ProgressPercent)
        {
            var previousPercent = task.ProgressPercent;
            task.ProgressPercent = input.ProgressPercent.Value;
            await _percentageLogs.AddAsync(new TaskPercentageLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = actorEmployeeId, PreviousPercent = previousPercent,
                NewPercent = task.ProgressPercent, Source = TaskPercentageLogSources.ManualEdit,
                ClockingSessionId = null, Reason = input.Reason?.Trim(), ChangedAt = now
            }, ct);
        }

        task.UpdatedAt = now;

        if (newValues.Count > 0)
        {
            await _editLogs.AddAsync(new TaskEditLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = actorEmployeeId, Source = editLogSource, EditRequestId = approvalRequestId,
                OldValuesJson = JsonSerializer.Serialize(oldValues),
                NewValuesJson = JsonSerializer.Serialize(newValues),
                Reason = input.Reason?.Trim(), ChangedAt = now
            }, ct);
        }

        return Result.Success();
    }

    public void Delete(WorkTask trackedTask) => _tasks.Remove(trackedTask);

    public void Restore(WorkTask trackedTask)
    {
        trackedTask.IsDeleted = false;
        trackedTask.DeletedAt = null;
        _tasks.Update(trackedTask);
    }

    private async Task<Domain.Features.WorkManagement.Tasks.Entities.TaskStatus?> FindDefaultStatusAsync(
        Guid tenantId, Guid projectId, CancellationToken ct)
    {
        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, projectId, ct);
        return statuses.Where(s => s.Category == TaskStatusCategories.NotStarted).OrderBy(s => s.DisplayOrder).FirstOrDefault()
            ?? statuses.Where(s => s.Category == TaskStatusCategories.Active).OrderBy(s => s.DisplayOrder).FirstOrDefault();
    }
}
