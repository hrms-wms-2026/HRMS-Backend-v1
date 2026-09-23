using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

public class CompleteSprintCommandHandler : IRequestHandler<CompleteSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IProjectMemberRepository _members;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        IProjectMemberRepository members, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _tasks = tasks;
        _statuses = statuses;
        _members = members;
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

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can complete sprints.");

        if (request.Disposition == "sprint")
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, request.TargetSprintId!.Value, ct);
            if (targetSprint is null || targetSprint.ObjectiveId != objective.Id)
                return Result<SprintResponse>.Failure("Target sprint must belong to the same milestone.", 422);
        }

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var task in tasks)
            {
                var status = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, innerCt);
                if (status is not null && status.MarksTaskComplete) continue;

                task.SprintId = request.Disposition == "sprint" ? request.TargetSprintId : null;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                _tasks.Update(task);
            }

            sprint.Status = SprintStatuses.Complete;
            sprint.CompletedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            var members = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, innerCt);
            foreach (var member in members)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, member.EmployeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_completed",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                    "sprint", sprint.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
