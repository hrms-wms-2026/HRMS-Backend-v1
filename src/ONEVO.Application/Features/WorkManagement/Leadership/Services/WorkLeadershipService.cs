using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed class WorkLeadershipService(
    IObjectiveRepository objectives,
    IProjectMemberRepository members,
    ITaskCreationRequestRepository taskCreationRequests,
    ITaskEditRequestRepository taskEditRequests,
    IObjectiveChangeRequestRepository objectiveChangeRequests,
    ITaskStatusChangeRequestRepository statusChangeRequests,
    ITaskStatusChangeAccessService statusChangeAccess,
    ILogger<WorkLeadershipService> logger) : IWorkLeadershipService
{
    public Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
        => objectives.AnyActiveOwnedAsync(tenantId, employeeId, legalEntityId, ct);

    public async Task<bool> HasPendingWorkApprovalsAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
    {
        if (await taskCreationRequests.HasPendingForOwnerEmployeeIdAsync(tenantId, employeeId, ct))
            return true;
        if (await taskEditRequests.HasPendingForOwnerEmployeeIdAsync(tenantId, employeeId, ct))
            return true;
        if (await objectiveChangeRequests.HasPendingForApproverAsync(tenantId, employeeId, ct))
            return true;

        // No tenant-wide EXISTS is possible for status-template changes without also knowing which
        // projects the caller can decide for, so this checks the (typically small) set of distinct
        // projects with a pending request and stops at the first one resolving CanEditDirectly.
        var pending = await statusChangeRequests.ListAllPendingAsync(tenantId, ct);
        foreach (var projectId in pending.Select(r => r.ProjectId).Distinct())
        {
            var access = await statusChangeAccess.ResolveAsync(tenantId, projectId, employeeId, ct);
            if (access is { CanEditDirectly: true })
                return true;
        }

        return false;
    }

    public async Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
    {
        var owned = await objectives.ListActiveOwnedIdsAsync(tenantId, employeeId, legalEntityId, ct);
        if (owned.Count == 0)
            return LedWorkScope.Empty;

        var projectIds = owned.Select(o => o.ProjectId).Distinct().ToList();
        var tree = await objectives.ListActiveTreeForProjectsAsync(tenantId, projectIds, ct);
        var membership = await members.ListActiveMembershipObjectiveIdsAsync(tenantId, employeeId, projectIds, ct);

        var scope = LedWorkScopeBuilder.Build(owned.Select(o => o.ObjectiveId).ToList(), tree, membership);
        if (scope.DroppedOwnedObjectiveIds.Count > 0)
        {
            // Data gap, not an error: the employee owns a module but holds no membership that lets
            // TaskAccessResolver open it (owners normally get one at create/transfer). Dropped so
            // the dashboard never lists a task that would 404 (My Team spec §8.3.2 step 7).
            logger.LogInformation(
                "Work I Lead: {Count} owned module(s) dropped for employee {EmployeeId} - no membership access: {ObjectiveIds}",
                scope.DroppedOwnedObjectiveIds.Count, employeeId, scope.DroppedOwnedObjectiveIds);
        }
        return scope;
    }
}
