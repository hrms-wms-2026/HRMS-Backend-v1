using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed class WorkLeadershipService(
    IObjectiveRepository objectives,
    IProjectMemberRepository members,
    IWorkApprovalRequestRepository approvalRequests,
    IWorkApprovalEligibility eligibility,
    ILogger<WorkLeadershipService> logger) : IWorkLeadershipService
{
    public Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
        => objectives.AnyActiveOwnedAsync(tenantId, employeeId, legalEntityId, ct);

    /// <summary>My Team's capability gate (backend merge plan §2/§4): HR-sourced pending approvals
    /// are a flat, no-staleness-risk check, evaluated first. Hierarchy-sourced ones delegate to
    /// IWorkApprovalEligibility.ListDecidableAcrossLedProjectsAsync - the exact same method every
    /// work.* ITeamActionSource calls (with its own narrower ActionType subset) - filtered by the
    /// exact same MyTeamApprovalActionTypes.All, so this gate can never disagree with what the
    /// Action Center actually shows: they are the same call, just a different filter and an
    /// Any()-vs-full-list read of the result.</summary>
    public async Task<bool> HasPendingWorkApprovalsAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
    {
        if (await approvalRequests.HasPendingForHrApproverAsync(tenantId, employeeId, MyTeamApprovalActionTypes.All, ct))
            return true;

        var decidable = await eligibility.ListDecidableAcrossLedProjectsAsync(tenantId, employeeId, legalEntityId, MyTeamApprovalActionTypes.All, ct);
        return decidable.Count > 0;
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
