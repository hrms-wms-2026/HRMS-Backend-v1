using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;

public class CreateTaskStatusChangeRequestCommandHandler
    : IRequestHandler<CreateTaskStatusChangeRequestCommand, Result<TaskStatusChangeRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskStatusChangeRequestRepository _requests;
    private readonly ITaskStatusChangeAccessService _access;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskStatusChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        ITaskStatusRepository statuses, ITaskStatusChangeRequestRepository requests,
        ITaskStatusChangeAccessService access, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _statuses = statuses;
        _requests = requests;
        _access = access;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskStatusChangeRequestResponse>> Handle(
        CreateTaskStatusChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskStatusChangeRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskStatusChangeRequestResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<TaskStatusChangeRequestResponse>.NotFound("Project not found.");

        var access = await _access.ResolveAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (access is null)
            return Result<TaskStatusChangeRequestResponse>.NotFound("Project has no default milestone.");
        if (access.CanEditDirectly)
            return Result<TaskStatusChangeRequestResponse>.Failure(
                "You can edit task statuses directly - no request needed.", 400);
        if (!access.CanRequest)
            return Result<TaskStatusChangeRequestResponse>.Forbidden(
                "Only members of this project's modules can request task status changes.");

        var shapeError = TaskStatusChangeSetRules.Validate(request.Changes);
        if (shapeError is not null)
            return Result<TaskStatusChangeRequestResponse>.Failure(shapeError, 422);

        // Dry-run against the live template (untracked rows, nothing is persisted) so a request that
        // is already stale or would break an invariant is refused up front instead of queued.
        var current = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var dryRun = TaskStatusChangeSetApplier.Apply(
            current, request.Changes, tenantId, project.Id, _currentUser.UserId, DateTimeOffset.UtcNow);
        if (dryRun.Outcome == TaskStatusChangeApplyOutcome.Stale)
            return Result<TaskStatusChangeRequestResponse>.Conflict(
                dryRun.Message + " Reopen the editor to load the latest statuses.");
        if (dryRun.Outcome == TaskStatusChangeApplyOutcome.Invalid)
            return Result<TaskStatusChangeRequestResponse>.Failure(dryRun.Message!, 422);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var requesterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";
        var approverIds = await _access.ListApproverEmployeeIdsAsync(tenantId, access.RootObjective, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new TaskStatusChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
                RequestedByEmployeeId = callerEmployeeId.Value,
                ChangesJson = JsonSerializer.Serialize(request.Changes),
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                Status = TaskStatusChangeRequestStatuses.Pending,
                CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);

            foreach (var approverId in approverIds)
            {
                var approver = await _membership.GetActiveAssigneeAsync(tenantId, approverId, innerCt);
                if (approver is null) continue;
                await _notifications.SendTemplatedAsync(
                    tenantId, approver.UserId, "work_task_status_change_request_created",
                    new Dictionary<string, string>
                    {
                        ["requesterName"] = requesterDisplayName,
                        ["projectName"] = project.Name
                    },
                    "task_status_change_request", entity.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusChangeRequestResponse>.Success(
                TaskStatusChangeRequestResponse.From(entity, requesterDisplayName, canDecide: false, canCancel: true));
        }, ct);
    }
}
