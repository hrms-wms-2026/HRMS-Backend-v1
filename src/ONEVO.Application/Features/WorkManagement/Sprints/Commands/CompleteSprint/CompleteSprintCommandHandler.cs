using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

public class CompleteSprintCommandHandler : IRequestHandler<CompleteSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IProjectRepository _projects;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ISprintRepository sprints, IWorkTaskRepository tasks, ITaskStatusRepository statuses, IProjectRepository projects,
        ISprintAccessService access, ISprintActivityLogRepository logs, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _tasks = tasks;
        _statuses = statuses;
        _projects = projects;
        _access = access;
        _logs = logs;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CompleteSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.Disposition is not ("backlog" or "sprint"))
            return Result<SprintResponse>.Failure("Unrecognized disposition.", 422);

        if (request.Disposition == "sprint" && request.TargetSprintId is null)
            return Result<SprintResponse>.Failure("A target sprint is required when moving tasks to another sprint.", 422);

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        if (!await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only the sprint's creator or an owner of one of its tasks' modules can complete this sprint.");

        if (request.Disposition == "sprint")
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, request.TargetSprintId!.Value, ct);
            if (targetSprint is null || targetSprint.ProjectId != sprint.ProjectId || targetSprint.Id == sprint.Id
                || targetSprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result<SprintResponse>.Failure("Target sprint must be another Draft or Active sprint in the same project.", 422);
        }

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var fromStatus = sprint.Status;

            var project = await _projects.GetByIdForTenantAsync(tenantId, sprint.ProjectId, innerCt);
            var audience = await _access.GetAudienceEmployeeIdsAsync(tenantId, sprint.Id, innerCt);

            var movedTaskIds = new List<Guid>();
            foreach (var task in tasks)
            {
                var status = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, innerCt);
                if (status is not null && status.MarksTaskComplete) continue;

                task.SprintId = request.Disposition == "sprint" ? request.TargetSprintId : null;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                _tasks.Update(task);
                movedTaskIds.Add(task.Id);
            }

            sprint.Status = SprintStatuses.Complete;
            sprint.CompletedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Completed, fromStatus, SprintStatuses.Complete,
                new { disposition = request.Disposition, targetSprintId = request.TargetSprintId, movedTaskIds }), innerCt);

            foreach (var employeeId in audience)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, employeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_completed",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = project?.Name ?? "the project" },
                    "sprint", sprint.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
