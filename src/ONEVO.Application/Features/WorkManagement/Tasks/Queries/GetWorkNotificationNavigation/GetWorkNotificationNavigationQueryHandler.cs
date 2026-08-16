using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetWorkNotificationNavigation;

public class GetWorkNotificationNavigationQueryHandler
    : IRequestHandler<GetWorkNotificationNavigationQuery, Result<WorkNotificationNavigationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskCreationRequestRepository _taskRequests;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;

    public GetWorkNotificationNavigationQueryHandler(
        ICurrentUser currentUser,
        IWorkTaskRepository tasks,
        ITaskCreationRequestRepository taskRequests,
        IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _taskRequests = taskRequests;
        _changeRequests = changeRequests;
        _objectives = objectives;
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
            "task_creation_request" => await FromTaskCreationRequestAsync(tenantId, request.RelatedEntityId, ct),
            "objective_change_request" or "allocation_extend" =>
                await FromChangeRequestAsync(tenantId, request.RelatedEntityId, ct),
            _ => Result<WorkNotificationNavigationResponse>.Failure(
                "Unsupported related entity type for Work Management navigation.")
        };
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromTaskAsync(
        Guid tenantId, Guid taskId, CancellationToken ct)
    {
        var task = await _tasks.GetByIdForTenantAsync(tenantId, taskId, ct);
        if (task is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            objective.ProjectId, objective.Id, task.Id, "board"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromTaskCreationRequestAsync(
        Guid tenantId, Guid requestId, CancellationToken ct)
    {
        var pending = await _taskRequests.GetByIdForTenantAsync(tenantId, requestId, ct);
        if (pending is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Task creation request not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, pending.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            objective.ProjectId, objective.Id, pending.CreatedTaskId, "board"));
    }

    private async Task<Result<WorkNotificationNavigationResponse>> FromChangeRequestAsync(
        Guid tenantId, Guid requestId, CancellationToken ct)
    {
        var change = await _changeRequests.GetByIdForTenantAsync(tenantId, requestId, ct);
        if (change is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Change request not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, change.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

        return Result<WorkNotificationNavigationResponse>.Success(new(
            objective.ProjectId, objective.Id, null, "approvals"));
    }
}
