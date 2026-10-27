using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;

public interface IWorkApprovalRequestRepository
{
    Task AddAsync(WorkApprovalRequest request, CancellationToken ct = default);
    Task<WorkApprovalRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<WorkApprovalRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<bool> HasPendingAsync(Guid tenantId, string targetType, Guid targetId, string actionType, CancellationToken ct = default);
    /// <summary>Of the given targets, the ones with any pending request (any action type) against them - used to badge a list of tasks/modules as "pending approval".</summary>
    Task<IReadOnlySet<Guid>> GetPendingTargetIdsAsync(Guid tenantId, string targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken ct = default);
    /// <summary>Newest first. requestedByEmployeeId and status are optional filters.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListByProjectAsync(
        Guid tenantId, Guid projectId, Guid? requestedByEmployeeId, string? status, CancellationToken ct = default);
    /// <summary>Tracked pending requests of one action type in a project - used by conflict sweeps.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListTrackedPendingByActionAsync(
        Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default);

    /// <summary>My Team capability gate (backend merge plan §2): is there a pending, HR-sourced
    /// request of one of actionTypes awaiting this employee's decision, anywhere in the tenant? HR
    /// approvals are resolved once at submit time and never re-checked against a tree, so this is a
    /// flat, cheap, no-staleness-risk check - evaluate before the hierarchy-based project loop.</summary>
    Task<bool> HasPendingForHrApproverAsync(
        Guid tenantId, Guid approverEmployeeId, IReadOnlySet<string> actionTypes, CancellationToken ct = default);

    /// <summary>Content companion to <see cref="HasPendingForHrApproverAsync"/> - the actual pending
    /// HR-sourced rows for this approver, not just whether any exist. WM-04 fix (2026-10-12): the
    /// gate and the My Team cross-project aggregator's content must never disagree - an HR-sourced
    /// approver who owns zero modules anywhere was previously invisible to
    /// WorkApprovalEligibility.ListDecidableAcrossLedProjectsAsync even though the gate correctly
    /// flagged them as having something pending.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListPendingForHrApproverAsync(
        Guid tenantId, Guid approverEmployeeId, IReadOnlySet<string> actionTypes, CancellationToken ct = default);

    /// <summary>Of the given projects, the ones with at least one pending request whose ActionType is
    /// in actionTypes (backend merge plan §2's bounded pre-check) - narrows a per-project
    /// LoadTreeAsync+CanDecide loop down to projects that actually have something to evaluate,
    /// instead of every project the caller merely owns a module in.</summary>
    Task<IReadOnlySet<Guid>> ListProjectsWithPendingAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, IReadOnlySet<string> actionTypes, CancellationToken ct = default);

    /// <summary>SELECT ... FOR UPDATE on the request row while it is still pending: true when this transaction now holds
    /// it, false when another decision got there first. A concurrent decider waits on the lock and then sees the row is
    /// no longer pending (meetings-as-tasks spec 5.2 "a conditional pending -> approved update"). Needs a transaction.</summary>
    Task<bool> LockIfPendingAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(WorkApprovalRequest request);
}
