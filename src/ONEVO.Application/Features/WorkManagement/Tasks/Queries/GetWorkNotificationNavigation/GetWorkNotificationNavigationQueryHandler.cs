using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;

public class GetWorkNotificationNavigationQueryHandler
    : IRequestHandler<GetWorkNotificationNavigationQuery, Result<WorkNotificationNavigationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly IWorkApprovalRequestRepository _workApprovals;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly ISprintRepository _sprints;
    private readonly ITaskAccessResolver? _taskAccess;

    public GetWorkNotificationNavigationQueryHandler(
        ICurrentUser currentUser,
        IWorkTaskRepository tasks,
        IWorkApprovalRequestRepository workApprovals,
        IObjectiveRepository objectives,
        IProjectMemberInvitationRepository invitations,
        ISprintRepository sprints,
        ITaskAccessResolver? taskAccess = null)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _workApprovals = workApprovals;
        _objectives = objectives;
        _invitations = invitations;
        _sprints = sprints;
        _taskAccess = taskAccess;
    }

    public async Task<Result<WorkNotificationNavigationResponse>> Handle(
        GetWorkNotificationNavigationQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkNotificationNavigationResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var type = request.RelatedEntityType.Trim().ToLowerInvariant();

        return type switch
        {
            "task" => await FromTaskAsync(tenantId, request.RelatedEntityId, ct),
            // Old task_creation/edit_request and objective_change_request/allocation_extend bell
            // notifications carry ids the data migrations kept, so they open the same Approvals tab as
            // the engine's work_approval_request.
            "work_approval_request" or "task_creation_request" or "task_edit_request"
                or "objective_change_request" or "allocation_extend" =>
                await FromWorkApprovalRequestAsync(tenantId, request.RelatedEntityId, ct),
            "module" => await FromModuleAsync(tenantId, request.RelatedEntityId, ct),
            "sprint" => await FromSprintAsync(tenantId, request.RelatedEntityId, ct),
            "project_member_invitation" =>
                await FromInvitationAsync(tenantId, request.RelatedEntityId, ct),
            "task_status_change_request" =>
                await FromStatusChangeRequestAsync(tenantId, request.RelatedEntityId, ct),
            _ => Result<WorkNotificationNavigationResponse>.Failure(
                "Unsupported related entity type for Work Management navigation.")
        };
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromWorkApprovalRequestAsync(
        Guid tenantId, Guid requestId, CancellationToken ct)
    {
        var approval = await _workApprovals.GetTrackedByIdForTenantAsync(tenantId, requestId, ct);
        if (approval is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Approval request not found.");

        var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, approval.ProjectId, ct);
        if (root is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(approval.ProjectId, root.Id, null, "approvals"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromStatusChangeRequestAsync(
        Guid tenantId, Guid requestId, CancellationToken ct)
    {
        // Old bell notifications carry task_status_change_request ids, which the data migration kept.
        var change = await _workApprovals.GetTrackedByIdForTenantAsync(tenantId, requestId, ct);
        if (change is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Task status change request not found.");

        var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, change.ProjectId, ct);
        if (root is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            change.ProjectId, root.Id, null, "approvals"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromInvitationAsync(
        Guid tenantId, Guid invitationId, CancellationToken ct)
    {
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, invitationId, ct);
        if (invitation is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Invitation not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, invitation.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            objective.ProjectId, objective.Id, null, "tree"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromTaskAsync(
        Guid tenantId, Guid taskId, CancellationToken ct)
    {
        if (_taskAccess is not null)
        {
            var access = await _taskAccess.ResolveViewableTaskAsync(
                tenantId, _currentUser.UserId, taskId, ct);
            if (!access.IsSuccess)
                return Result<WorkNotificationNavigationResponse>.Failure(
                    access.Error!, access.StatusCode ?? 404);
        }
        var task = await _tasks.GetByIdForTenantAsync(tenantId, taskId, ct);
        if (task is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            objective.ProjectId, objective.Id, task.Id, "board"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromModuleAsync(Guid tenantId, Guid moduleId, CancellationToken ct)
    {
        var module = await _objectives.GetByIdForTenantAsync(tenantId, moduleId, ct);
        return module is null
            ? Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.")
            : Result<WorkNotificationNavigationResponse>.Success(new(module.ProjectId, module.Id, null, "tree"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct)
    {
        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, sprintId, ct);
        if (sprint is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Sprint not found.");
        var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, sprint.ProjectId, ct);
        return root is null
            ? Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.")
            : Result<WorkNotificationNavigationResponse>.Success(new(sprint.ProjectId, root.Id, null, "tree"));
    }
}
