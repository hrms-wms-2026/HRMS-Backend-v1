using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatusChangeRequest;

/// <summary>
/// Files a project.status_template_change approval request. The root (default) module owner is the
/// only approver and edits statuses directly instead, so this is for everyone else in the project.
/// </summary>
public class CreateTaskStatusChangeRequestCommandHandler
    : IRequestHandler<CreateTaskStatusChangeRequestCommand, Result<TaskStatusChangeRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskStatusChangeAccessService _access;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskStatusChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        ITaskStatusRepository statuses, ITaskStatusChangeAccessService access, IWorkApprovalEngine approvals,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _statuses = statuses;
        _access = access;
        _approvals = approvals;
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
        var payload = new TaskStatusTemplateChangePayload(
            request.Changes, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, project.Id, callerEmployeeId.Value,
                WorkActionTypes.ProjectStatusTemplateChange, WorkTargetTypes.Project,
                TargetId: null, TargetTitle: project.Name,
                TargetModuleId: access.RootObjective.Id, PositionModuleId: access.RootObjective.Id,
                PayloadJson: JsonSerializer.Serialize(payload), TargetUpdatedAt: null), innerCt);
            if (!decision.IsSuccess)
                return Result<TaskStatusChangeRequestResponse>.Failure(decision.Error!, decision.StatusCode ?? 400);
            if (decision.Value!.IsDirect) // cannot happen: access said the caller is not the root owner
                return Result<TaskStatusChangeRequestResponse>.Failure(
                    "You can edit task statuses directly - no request needed.", 400);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusChangeRequestResponse>.Success(new TaskStatusChangeRequestResponse(
                decision.Value.ApprovalRequestId!.Value, project.Id, WorkApprovalRequestStatuses.Pending,
                callerEmployeeId.Value, requesterDisplayName, payload.Note, payload.Changes,
                DateTimeOffset.UtcNow, null, null, null, CanDecide: false, CanCancel: true));
        }, ct);
    }
}
