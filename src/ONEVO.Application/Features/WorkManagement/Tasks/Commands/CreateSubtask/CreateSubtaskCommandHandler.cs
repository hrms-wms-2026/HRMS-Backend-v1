using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;

public sealed class CreateSubtaskCommandHandler : IRequestHandler<CreateSubtaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSubtaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        ITaskAssignmentRepository assignments, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _statuses = statuses;
        _assignments = assignments;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(CreateSubtaskCommand request, CancellationToken ct)
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

        var parent = await _tasks.GetByIdForTenantAsync(tenantId, request.ParentTaskId, ct);
        if (parent is null)
            return Result<WorkTaskResponse>.NotFound("Parent task not found.");
        if (parent.ParentTaskId is not null)
            return Result<WorkTaskResponse>.Conflict("A subtask cannot itself have subtasks.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, parent.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create subtasks.");

        ONEVO.Domain.Features.CoreHr.Entities.Employee? assignee = null;
        if (request.AssigneeEmployeeId is { } assigneeEmployeeId)
        {
            assignee = await _membership.GetActiveAssigneeAsync(tenantId, assigneeEmployeeId, ct);
            if (assignee is null)
                return Result<WorkTaskResponse>.Failure("The assignee must be an active employee in this tenant.");
        }

        var project = await _projects.GetByIdForTenantAsync(tenantId, parent.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Project not found.");

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var defaultStatus = statuses.Where(s => s.Category == TaskStatusCategories.NotStarted).OrderBy(s => s.DisplayOrder).FirstOrDefault()
            ?? statuses.Where(s => s.Category == TaskStatusCategories.Active).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, parent.ProjectId, innerCt);
            var now = DateTimeOffset.UtcNow;
            var subtask = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = parent.ProjectId, ParentTaskId = parent.Id,
                ObjectiveId = parent.ObjectiveId, ShortId = $"{project.Identifier}-{taskNumber}",
                StatusId = defaultStatus.Id, Title = request.Title.Trim(), CategoryId = parent.CategoryId,
                Priority = request.Priority ?? WorkTaskPriorities.Medium, DueDate = request.DueDate,
                CompletedHours = 0m, ProgressPercent = 0, CreatedById = userId, CreatedAt = now
            };

            await _tasks.AddAsync(subtask, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            if (assignee is not null)
            {
                await _assignments.AddAsync(new TaskAssignment
                {
                    Id = Guid.NewGuid(), TaskId = subtask.Id, UserId = assignee.UserId, EmployeeId = assignee.Id,
                    AssignedById = callerEmployeeId.Value, AssignedAt = now
                }, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
            }

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                subtask.Id, subtask.ObjectiveId, subtask.ShortId, subtask.Title, subtask.Description,
                subtask.CategoryId, subtask.StatusId, subtask.Priority, subtask.StoryPoints,
                subtask.DueDate, subtask.EstimatedHours, subtask.CompletedHours, subtask.ProgressPercent, subtask.SprintId,
                AssigneeEmployeeIds: assignee is not null ? new[] { assignee.Id } : Array.Empty<Guid>(),
                ParentTaskId: subtask.ParentTaskId));
        }, ct);
    }
}
