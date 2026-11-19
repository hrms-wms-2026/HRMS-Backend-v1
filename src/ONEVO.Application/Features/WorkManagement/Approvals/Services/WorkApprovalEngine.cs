using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class WorkApprovalEngine : IWorkApprovalEngine
{
    /// <summary>Every Work Management user holds it, so the HR fallback resolves the reporting-line
    /// manager instead of failing on a permission most managers were never granted.</summary>
    public const string HrFallbackPermission = "projects:access";

    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IProjectRepository _projects;
    private readonly IEmployeeAuthorityResolver _authority;

    public WorkApprovalEngine(
        IWorkHierarchyService hierarchy, IWorkApprovalRequestRepository requests, IWorkNotificationEngine notifications,
        IProjectRepository projects, IEmployeeAuthorityResolver authority)
    {
        _hierarchy = hierarchy;
        _requests = requests;
        _notifications = notifications;
        _projects = projects;
        _authority = authority;
    }

    public async Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default)
    {
        var tree = await _hierarchy.LoadTreeAsync(action.TenantId, action.ProjectId, ct);
        var targetModule = tree.Get(action.TargetModuleId);
        if (targetModule is null)
            return Result<ApprovalDecision>.NotFound("The module this action belongs to was not found.");

        // Spec §5.2 rule 1 + §5.4: a Module with no creator position is a root Module. Its own
        // owner has nobody above them in the tree (so the HR approver decides); anyone else editing
        // it goes to the root owner.
        var position = action.PositionModuleId;
        var useHr = false;
        if (position is null)
        {
            if (action.TargetType == WorkTargetTypes.Module && targetModule.OwnerId == action.ActorEmployeeId)
                useHr = true;
            else
                position = action.TargetModuleId;
        }

        Guid? approver = null;
        var source = WorkApprovalSources.Hierarchy;
        if (!useHr)
        {
            if (tree.Get(position!.Value) is null)
                return Result<ApprovalDecision>.NotFound("The module this action belongs to was not found.");
            if (tree.IsAtOrAbove(action.ActorEmployeeId, position.Value))
                return Result<ApprovalDecision>.Success(ApprovalDecision.Direct);
            approver = await _hierarchy.FindActiveHolderAsync(action.TenantId, tree, position.Value, action.ActorEmployeeId, ct);
        }

        if (approver is null)
        {
            approver = await ResolveHrApproverAsync(action, ct);
            source = WorkApprovalSources.Hr;
            position = null;
        }
        if (approver is null)
            return Result<ApprovalDecision>.UnprocessableEntity("No eligible approver was found for this action.");

        if (action.TargetId is { } targetId
            && await _requests.HasPendingAsync(action.TenantId, action.TargetType, targetId, action.ActionType, ct))
            return Result<ApprovalDecision>.Conflict("A request for this change is already waiting for approval.");

        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = action.TenantId, ProjectId = action.ProjectId,
            ActionType = action.ActionType, TargetType = action.TargetType, TargetId = action.TargetId,
            TargetTitle = action.TargetTitle, PositionObjectiveId = position, ApproverSource = source,
            ApproverEmployeeId = approver.Value, RequestedByEmployeeId = action.ActorEmployeeId,
            PayloadJson = action.PayloadJson, Status = WorkApprovalRequestStatuses.Pending,
            TargetUpdatedAtSnapshot = action.TargetUpdatedAt, CreatedAt = DateTimeOffset.UtcNow
        };
        await _requests.AddAsync(request, ct);

        await _notifications.NotifyAsync(new WorkNotificationEvent(
            action.TenantId, action.ProjectId, action.ActorEmployeeId, WorkNotificationKinds.Requested,
            action.ActionType, action.TargetType, action.TargetId, action.TargetTitle, request.Id, [approver.Value]), ct);

        return Result<ApprovalDecision>.Success(ApprovalDecision.Pending(request.Id, approver.Value));
    }

    private async Task<Guid?> ResolveHrApproverAsync(WorkAction action, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(action.TenantId, action.ProjectId, ct);
        if (project is null)
            return null;

        var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            action.ActorEmployeeId, project.OwningLegalEntityId, HrFallbackPermission,
            EmployeeAuthorityPurpose.EmployeeLifecycleApproval), ct);

        return route.IsSuccess && route.Value!.ApproverEmployeeId != action.ActorEmployeeId
            ? route.Value.ApproverEmployeeId
            : null;
    }
}
