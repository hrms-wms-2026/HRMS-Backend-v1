using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;

public sealed class ConvertTaskToSubtaskCommandHandler : IRequestHandler<ConvertTaskToSubtaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public ConvertTaskToSubtaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, ITaskAssignmentRepository assignments,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _assignments = assignments;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(ConvertTaskToSubtaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<WorkTaskResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        if (request.TaskId == request.NewParentTaskId)
            return Result<WorkTaskResponse>.Conflict("A task cannot become its own subtask.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var newParent = await _tasks.GetByIdForTenantAsync(tenantId, request.NewParentTaskId, ct);
        if (newParent is null)
            return Result<WorkTaskResponse>.NotFound("Target task not found.");

        // Keeps the existing one-level nesting rule (see CreateSubtaskCommandHandler): the target
        // must itself be a top-level task, never another subtask. This also rules out every cycle -
        // the only way `newParent` could be a descendant of `task` is for it to already have a
        // ParentTaskId, which this same check rejects.
        if (newParent.ParentTaskId is not null)
            return Result<WorkTaskResponse>.Conflict("A subtask cannot itself have subtasks.");

        // CategoryId/StatusId below are reused verbatim from the task's own current values, which are
        // only valid within the same project's template - cross-project conversion isn't supported yet.
        if (newParent.ProjectId != task.ProjectId)
            return Result<WorkTaskResponse>.Conflict("Converting a task into a subtask in a different project is not supported yet.");

        var targetObjective = await _objectives.GetByIdForTenantAsync(tenantId, newParent.ObjectiveId, ct);
        if (targetObjective is null || !targetObjective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, targetObjective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create subtasks.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            // ClickUp-style flatten: a task's own subtasks can't survive it becoming a subtask itself
            // (one level of nesting only), so they become siblings under the new parent instead of
            // being orphaned or blocking the conversion.
            var children = await _tasks.GetTrackedByParentTaskIdAsync(tenantId, task.Id, innerCt);
            foreach (var child in children)
            {
                child.ParentTaskId = newParent.Id;
                child.ObjectiveId = targetObjective.Id;
                child.UpdatedAt = now;
            }

            task.ParentTaskId = newParent.Id;
            task.ObjectiveId = targetObjective.Id;
            // Subtasks never carry a sprint in this model (CreateSubtaskCommandHandler never sets one
            // either) - clear it rather than leave a stale reference to a sprint under the old objective.
            task.SprintId = null;
            task.UpdatedAt = now;

            await _unitOfWork.SaveChangesAsync(innerCt);

            var assignments = await _assignments.GetByTaskIdAsync(task.Id, innerCt);
            var assigneeIds = assignments.Select(a => a.EmployeeId).ToList();

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
                AssigneeEmployeeIds: assigneeIds, ParentTaskId: task.ParentTaskId, CreatedAt: task.CreatedAt));
        }, ct);
    }
}
