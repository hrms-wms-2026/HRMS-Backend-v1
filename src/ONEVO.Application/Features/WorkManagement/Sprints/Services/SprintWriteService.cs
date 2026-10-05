using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public sealed class SprintWriteService : ISprintWriteService
{
    private readonly IProjectRepository _projects;
    private readonly ISprintRepository _sprints;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ISprintTaskAssignmentService _assignment;
    private readonly ISprintActivityLogRepository _logs;
    private readonly ISprintAudienceResolver _audience;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;

    public SprintWriteService(
        IProjectRepository projects, ISprintRepository sprints, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        ISprintTaskAssignmentService assignment, ISprintActivityLogRepository logs, ISprintAudienceResolver audience,
        IMilestoneMembershipCoordinator membership, INotificationDispatcher notifications)
    {
        _projects = projects;
        _sprints = sprints;
        _tasks = tasks;
        _statuses = statuses;
        _assignment = assignment;
        _logs = logs;
        _audience = audience;
        _membership = membership;
        _notifications = notifications;
    }

    // ---- Create ----

    public async Task<Result> ValidateCreateAsync(Guid tenantId, Guid actorEmployeeId, SprintCreateInput input, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, input.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        // Dry run of the task moves on a throwaway Draft sprint - PrepareAsync never mutates.
        var probe = new Sprint { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, Status = SprintStatuses.Draft };
        var prepared = await _assignment.PrepareAsync(tenantId, probe, input.TaskIds, Array.Empty<Guid>(), actorEmployeeId, ct);
        return prepared.IsSuccess ? Result.Success() : Result.Failure(prepared.Error!, prepared.StatusCode ?? 400);
    }

    public async Task<Result<Sprint>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, SprintCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, input.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<Sprint>.NotFound("Project not found.");

        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = input.Name.Trim(), Goal = input.Goal?.Trim(),
            Status = SprintStatuses.Draft, CreatedById = creatorUserId, CreatedAt = DateTimeOffset.UtcNow,
            CreatorPositionObjectiveId = creatorPositionObjectiveId
        };

        var prepared = await _assignment.PrepareAsync(tenantId, sprint, input.TaskIds, Array.Empty<Guid>(), actorEmployeeId, ct);
        if (!prepared.IsSuccess)
            return Result<Sprint>.Failure(prepared.Error!, prepared.StatusCode ?? 400);
        var changes = prepared.Value!;

        // Capture each ToAdd task's pre-move sprint (Apply overwrites SprintId in place below), so
        // cross-sprint moves can log a tasks_removed entry on the sprint(s) they moved out of.
        var previousSprintGroups = changes.ToAdd
            .Where(t => t.SprintId is not null)
            .GroupBy(t => t.SprintId!.Value)
            .ToList();

        await _sprints.AddAsync(sprint, ct);
        _assignment.Apply(changes, sprint.Id);
        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, sprint.Id, actorEmployeeId, SprintActivityActions.Created, toStatus: SprintStatuses.Draft), ct);
        if (changes.ToAdd.Count > 0)
            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, actorEmployeeId, SprintActivityActions.TasksAdded,
                details: new { taskIds = changes.ToAdd.Select(t => t.Id) }), ct);
        foreach (var group in previousSprintGroups)
            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, group.Key, actorEmployeeId, SprintActivityActions.TasksRemoved,
                details: new { taskIds = group.Select(t => t.Id) }), ct);

        return Result<Sprint>.Success(sprint);
    }

    // ---- Edit ----

    public Task<Result> ValidateEditAsync(Sprint sprint, SprintEditInput input)
    {
        if (sprint.Status is SprintStatuses.Complete or SprintStatuses.Achieved)
            return Task.FromResult(Result.Conflict("This sprint has already ended and can no longer be edited."));

        if (sprint.Status == SprintStatuses.Draft && (input.StartDate is not null || input.EndDate is not null))
            return Task.FromResult(Result.Failure("A Draft sprint has no dates yet - start it to set dates.", 422));

        if (sprint.Status == SprintStatuses.Active && input.StartDate is not null && input.EndDate is not null
            && input.EndDate < input.StartDate)
            return Task.FromResult(Result.Failure("End date must not be before start date."));

        return Task.FromResult(Result.Success());
    }

    public async Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintEditInput input, CancellationToken ct = default)
    {
        var validation = await ValidateEditAsync(trackedSprint, input);
        if (!validation.IsSuccess)
            return validation;

        trackedSprint.Name = input.Name.Trim();
        trackedSprint.Goal = input.Goal?.Trim();
        if (trackedSprint.Status == SprintStatuses.Active && input.StartDate is not null && input.EndDate is not null)
        {
            trackedSprint.StartDate = input.StartDate;
            trackedSprint.EndDate = input.EndDate;
        }
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;

        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, trackedSprint.Id, actorEmployeeId, SprintActivityActions.Edited,
            details: new { name = trackedSprint.Name, goal = trackedSprint.Goal, startDate = trackedSprint.StartDate, endDate = trackedSprint.EndDate }), ct);
        return Result.Success();
    }

    // ---- Start ----

    public Task<Result> ValidateStartAsync(Guid tenantId, Sprint sprint, SprintStartInput input, CancellationToken ct = default)
    {
        if (input.EndDate < input.StartDate)
            return Task.FromResult(Result.Failure("End date must not be before start date."));

        if (sprint.Status != SprintStatuses.Draft)
            return Task.FromResult(Result.Conflict("Only a Draft sprint can be started."));

        return Task.FromResult(Result.Success());
    }

    public async Task<Result> ApplyStartAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintStartInput input, CancellationToken ct = default)
    {
        var validation = await ValidateStartAsync(tenantId, trackedSprint, input, ct);
        if (!validation.IsSuccess)
            return validation;

        trackedSprint.StartDate = input.StartDate;
        trackedSprint.EndDate = input.EndDate;
        if (input.Goal is not null) trackedSprint.Goal = input.Goal.Trim();
        trackedSprint.Status = SprintStatuses.Active;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;

        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, trackedSprint.Id, actorEmployeeId, SprintActivityActions.Started,
            SprintStatuses.Draft, SprintStatuses.Active,
            new { startDate = input.StartDate, endDate = input.EndDate }), ct);
        return Result.Success();
    }

    // ---- Complete ----

    public async Task<Result> ValidateCompleteAsync(Guid tenantId, Sprint sprint, SprintCompleteInput input, CancellationToken ct = default)
    {
        if (input.Disposition is not ("backlog" or "sprint"))
            return Result.Failure("Unrecognized disposition.", 422);

        if (input.Disposition == "sprint" && input.TargetSprintId is null)
            return Result.Failure("A target sprint is required when moving tasks to another sprint.", 422);

        if (sprint.Status != SprintStatuses.Active)
            return Result.Conflict("Only an Active sprint can be completed.");

        if (input.Disposition == "sprint")
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, input.TargetSprintId!.Value, ct);
            if (targetSprint is null || targetSprint.ProjectId != sprint.ProjectId || targetSprint.Id == sprint.Id
                || targetSprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result.Failure("Target sprint must be another Draft or Active sprint in the same project.", 422);
        }

        return Result.Success();
    }

    public async Task<Result> ApplyCompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintCompleteInput input, CancellationToken ct = default)
    {
        var validation = await ValidateCompleteAsync(tenantId, trackedSprint, input, ct);
        if (!validation.IsSuccess)
            return validation;

        var fromStatus = trackedSprint.Status;
        var tasks = await _tasks.GetBySprintIdAsync(tenantId, trackedSprint.Id, ct);
        var audience = await _audience.GetAudienceEmployeeIdsAsync(tenantId, trackedSprint.Id, ct);

        var movedTaskIds = new List<Guid>();
        foreach (var task in tasks)
        {
            var status = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, ct);
            if (status is not null && status.MarksTaskComplete) continue;

            task.SprintId = input.Disposition == "sprint" ? input.TargetSprintId : null;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            _tasks.Update(task);
            movedTaskIds.Add(task.Id);
        }

        trackedSprint.Status = SprintStatuses.Complete;
        trackedSprint.CompletedAt = DateTimeOffset.UtcNow;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;

        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, trackedSprint.Id, actorEmployeeId, SprintActivityActions.Completed, fromStatus, SprintStatuses.Complete,
            new { disposition = input.Disposition, targetSprintId = input.TargetSprintId, movedTaskIds }), ct);

        if (input.Disposition == "sprint" && movedTaskIds.Count > 0)
            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, input.TargetSprintId!.Value, actorEmployeeId, SprintActivityActions.TasksAdded,
                details: new { taskIds = movedTaskIds }), ct);

        var completionTemplateCode = movedTaskIds.Count > 0 ? "work_sprint_incomplete" : "work_sprint_completed";
        await NotifyAudienceAsync(tenantId, trackedSprint, audience, completionTemplateCode, ct);
        return Result.Success();
    }

    // ---- Achieve ----

    public Task<Result> ValidateAchieveAsync(Guid tenantId, Sprint sprint, CancellationToken ct = default)
        => Task.FromResult(sprint.Status == SprintStatuses.Achieved
            ? Result.Conflict("This sprint has already been achieved.")
            : Result.Success());

    public async Task<Result> ApplyAchieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, CancellationToken ct = default)
    {
        var validation = await ValidateAchieveAsync(tenantId, trackedSprint, ct);
        if (!validation.IsSuccess)
            return validation;

        var fromStatus = trackedSprint.Status;
        trackedSprint.Status = SprintStatuses.Achieved;
        trackedSprint.AchievedAt = DateTimeOffset.UtcNow;
        trackedSprint.UpdatedAt = DateTimeOffset.UtcNow;

        await _logs.AddAsync(SprintActivityLogFactory.Create(
            tenantId, trackedSprint.Id, actorEmployeeId, SprintActivityActions.Achieved, fromStatus, SprintStatuses.Achieved), ct);

        var audience = await _audience.GetAudienceEmployeeIdsAsync(tenantId, trackedSprint.Id, ct);
        await NotifyAudienceAsync(tenantId, trackedSprint, audience, "work_sprint_achieved", ct);
        return Result.Success();
    }

    // ---- Delete ----

    public Result ValidateDelete(Sprint sprint)
        => sprint.Status is SprintStatuses.Complete or SprintStatuses.Achieved
            ? Result.Success()
            : Result.Conflict("Only a completed or achieved sprint can be deleted.");

    public async Task ApplyDeleteAsync(Guid tenantId, Sprint trackedSprint, CancellationToken ct = default)
    {
        foreach (var task in await _tasks.GetBySprintIdAsync(tenantId, trackedSprint.Id, ct))
        {
            var tracked = await _tasks.GetTrackedByIdForTenantAsync(tenantId, task.Id, ct);
            if (tracked is null) continue;
            tracked.SprintId = null;
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
        }
        _sprints.Remove(trackedSprint);
    }

    private async Task NotifyAudienceAsync(Guid tenantId, Sprint sprint, IReadOnlyList<Guid> audience, string templateCode, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, sprint.ProjectId, ct);
        foreach (var employeeId in audience)
        {
            var assignee = await _membership.GetActiveAssigneeAsync(tenantId, employeeId, ct);
            if (assignee is null) continue;

            await _notifications.SendTemplatedAsync(
                tenantId, assignee.UserId, templateCode,
                new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = project?.Name ?? "the project" },
                "sprint", sprint.Id, ct);
        }
    }
}
