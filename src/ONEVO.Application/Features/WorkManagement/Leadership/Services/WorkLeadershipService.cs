using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed class WorkLeadershipService(
    IObjectiveRepository objectives,
    IProjectMemberRepository members,
    ILogger<WorkLeadershipService> logger) : IWorkLeadershipService
{
    public Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
        => objectives.AnyActiveOwnedAsync(tenantId, employeeId, legalEntityId, ct);

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
