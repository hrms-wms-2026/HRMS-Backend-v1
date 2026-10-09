using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>The one place hierarchy-sourced Work Approval eligibility is evaluated for My Team
/// (backend merge plan §4/§7). Every My Team consumer - the capability gate
/// (WorkLeadershipService.HasPendingWorkApprovalsAsync) and every work.* ITeamActionSource - calls
/// ListDecidableAcrossLedProjectsAsync with its own ActionType subset instead of writing its own
/// project-discovery-and-eligibility logic, so they can never drift apart: the gate is simply
/// "is the result non-empty for MyTeamApprovalActionTypes.All" and a source's content is simply
/// "the result for that source's own subset, ordered and capped."</summary>
public interface IWorkApprovalEligibility
{
    /// <summary>Pending requests in one already-known project, of one of actionTypes, that
    /// employeeId can currently decide per the live tree - the exact logic
    /// ListProjectWorkApprovalsQueryHandler's real "inbox" scope uses.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListDecidableAsync(
        Guid tenantId, Guid projectId, Guid employeeId, IReadOnlySet<string> actionTypes, CancellationToken ct = default);

    /// <summary>Every decidable request of one of actionTypes, across every project employeeId owns
    /// at least one active module in (not ResolveLedScopeAsync's membership-intersected
    /// HeadModules - approval eligibility has no membership requirement, so applying that
    /// intersection here would silently drop decidable requests; backend merge plan §1/§2). Narrows
    /// to only the projects that actually have a pending in-scope row before loading any tree.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListDecidableAcrossLedProjectsAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, IReadOnlySet<string> actionTypes, CancellationToken ct = default);
}

public sealed class WorkApprovalEligibility(
    IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy, IObjectiveRepository objectives)
    : IWorkApprovalEligibility
{
    public async Task<IReadOnlyList<WorkApprovalRequest>> ListDecidableAsync(
        Guid tenantId, Guid projectId, Guid employeeId, IReadOnlySet<string> actionTypes, CancellationToken ct = default)
    {
        var tree = await hierarchy.LoadTreeAsync(tenantId, projectId, ct);
        var pending = await requests.ListByProjectAsync(tenantId, projectId, null, WorkApprovalRequestStatuses.Pending, ct);
        return pending
            .Where(r => actionTypes.Contains(r.ActionType) && WorkApprovalDecisionRules.CanDecide(tree, r, employeeId))
            .ToList();
    }

    public async Task<IReadOnlyList<WorkApprovalRequest>> ListDecidableAcrossLedProjectsAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, IReadOnlySet<string> actionTypes, CancellationToken ct = default)
    {
        var owned = await objectives.ListActiveOwnedIdsAsync(tenantId, employeeId, legalEntityId, ct);
        if (owned.Count == 0)
            return Array.Empty<WorkApprovalRequest>();

        var projectIds = owned.Select(o => o.ProjectId).Distinct().ToList();
        var projectsWithPending = await requests.ListProjectsWithPendingAsync(tenantId, projectIds, actionTypes, ct);
        if (projectsWithPending.Count == 0)
            return Array.Empty<WorkApprovalRequest>();

        var decidable = new List<WorkApprovalRequest>();
        foreach (var projectId in projectsWithPending)
            decidable.AddRange(await ListDecidableAsync(tenantId, projectId, employeeId, actionTypes, ct));
        return decidable;
    }
}
