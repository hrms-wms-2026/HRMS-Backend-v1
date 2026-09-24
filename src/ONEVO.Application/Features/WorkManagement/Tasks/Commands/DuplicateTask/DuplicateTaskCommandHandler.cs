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
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DuplicateTask;

public class DuplicateTaskCommandHandler : IRequestHandler<DuplicateTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly ISprintRepository _sprints;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskCommentRepository _comments;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly ITaskAssetLinker _assetLinker;

    public DuplicateTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, IProjectRepository projects, ITaskStatusRepository statuses,
        ISprintRepository sprints, ITaskAssignmentRepository assignments, ITaskCommentRepository comments,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership,
        ICalendarEventRepository calendarEvents, ITaskAssetLinker assetLinker)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _sprints = sprints;
        _assignments = assignments;
        _comments = comments;
        _slack = slack;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _calendarEvents = calendarEvents;
        _assetLinker = assetLinker;
    }

    public async Task<Result<WorkTaskResponse>> Handle(DuplicateTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var source = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (source is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var destinationObjective = await _objectives.GetByIdForTenantAsync(tenantId, request.DestinationObjectiveId, ct);
        if (destinationObjective is null || !destinationObjective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Destination module not found.");

        // CategoryId/StatusId below are reused verbatim from the source task, which is only valid
        // within the same project's template - cross-project duplication isn't supported yet.
        if (destinationObjective.ProjectId != source.ProjectId)
            return Result<WorkTaskResponse>.Conflict("Duplicating a task into a different project is not supported yet.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, destinationObjective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, destinationObjective.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Project not found.");

        var dueDate = request.CopyDueDate ? source.DueDate : null;

        var eventWindows = await _calendarEvents.ListActiveEventWindowsForObjectiveAsync(tenantId, destinationObjective.Id, ct);
        if (eventWindows.Count > 0)
        {
            if (dueDate is null)
                return Result<WorkTaskResponse>.Conflict(
                    $"This module is in active event(s) {string.Join(", ", eventWindows.Select(w => w.Name))}; a due date is required.");
            var bad = eventWindows.Where(w => dueDate < w.StartDate || dueDate > w.EndDate).ToList();
            if (bad.Count > 0)
                return Result<WorkTaskResponse>.Conflict(
                    $"Due date {dueDate:yyyy-MM-dd} is outside event window(s): " +
                    $"{string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.");
        }

        // A sprint only makes sense within the objective it belongs to - carry it over only when
        // duplicating in place; a cross-module copy always starts unsprinted.
        var sprintId = destinationObjective.Id == source.ObjectiveId ? source.SprintId : null;
        if (sprintId is not null)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, sprintId.Value, ct);
            if (sprint is null || sprint.ObjectiveId != destinationObjective.Id)
                sprintId = null;
            else if (sprint.Status == SprintStatuses.Achieved)
                return Result<WorkTaskResponse>.Conflict("This sprint has been achieved and is frozen.");
        }

        if (source.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, destinationObjective, ct: ct);
            if (source.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var defaultStatus = statuses.Where(s => s.Category == TaskStatusCategories.NotStarted).OrderBy(s => s.DisplayOrder).FirstOrDefault()
            ?? statuses.Where(s => s.Category == TaskStatusCategories.Active).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, project.Id, innerCt);
            var now = DateTimeOffset.UtcNow;
            // A duplicate always starts fresh: default (not-started) status, zero progress, no
            // completion timestamps, and never a subtask - even when the source was one.
            var copy = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, ObjectiveId = destinationObjective.Id,
                ShortId = $"{project.Identifier}-{taskNumber}",
                StatusId = defaultStatus.Id,
                Title = request.Title.Trim(), Description = source.Description,
                CategoryId = source.CategoryId, Priority = source.Priority, DueDate = dueDate,
                EstimatedHours = source.EstimatedHours, StoryPoints = source.StoryPoints,
                SprintId = sprintId, ParentTaskId = null,
                CompletedHours = 0m, ProgressPercent = 0, CreatedById = userId, CreatedAt = now
            };

            await _tasks.AddAsync(copy, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, copy.Id, copy.Description, innerCt);

            if (request.CopyAttachments)
                await _assetLinker.CopyAttachmentsAsync(tenantId, userId, source.Id, copy.Id, innerCt);

            if (request.CopyAssignees)
            {
                var sourceAssignments = await _assignments.GetByTaskIdAsync(source.Id, innerCt);
                foreach (var sourceAssignment in sourceAssignments)
                {
                    // Re-resolve through the coordinator rather than trusting the stored row: the
                    // employee may have gone inactive since, and UserId must come from the fresh
                    // lookup, not copied off the old assignment (same class of bug as the
                    // UserId/EmployeeId mixup fixed elsewhere in this codebase).
                    var assignee = await _membership.GetActiveAssigneeAsync(tenantId, sourceAssignment.EmployeeId, innerCt);
                    if (assignee is null)
                        continue;

                    await _assignments.AddAsync(new TaskAssignment
                    {
                        Id = Guid.NewGuid(), TaskId = copy.Id, UserId = assignee.UserId,
                        EmployeeId = assignee.Id, AssignedById = callerEmployeeId.Value, AssignedAt = now
                    }, innerCt);
                }
            }

            if (request.CopyComments)
            {
                var sourceComments = (await _comments.GetForTaskAsync(tenantId, source.Id, innerCt))
                    .Where(c => !c.IsDeleted)
                    .ToList();
                var idMap = new Dictionary<Guid, Guid>();
                foreach (var sourceComment in sourceComments)
                {
                    var newId = Guid.NewGuid();
                    idMap[sourceComment.Id] = newId;
                    Guid? parentId = sourceComment.ParentCommentId.HasValue && idMap.TryGetValue(sourceComment.ParentCommentId.Value, out var mappedParentId)
                        ? mappedParentId
                        : null;

                    await _comments.AddAsync(new TaskComment
                    {
                        Id = newId, TenantId = tenantId, TaskId = copy.Id, EmployeeId = sourceComment.EmployeeId,
                        ParentCommentId = parentId, Content = sourceComment.Content, IsEdited = sourceComment.IsEdited,
                        CreatedById = userId, CreatedAt = now
                    }, innerCt);
                }
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                copy.Id, copy.ObjectiveId, copy.ShortId, copy.Title, copy.Description,
                copy.CategoryId, copy.StatusId, copy.Priority, copy.StoryPoints,
                copy.DueDate, copy.EstimatedHours, copy.CompletedHours, copy.ProgressPercent, copy.SprintId,
                CreatedAt: copy.CreatedAt));
        }, ct);
    }
}
