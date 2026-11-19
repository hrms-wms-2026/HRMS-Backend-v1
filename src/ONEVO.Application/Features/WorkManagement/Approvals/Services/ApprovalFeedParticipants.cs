using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Who sees an approval request on the Approvals page and in its comment thread.
/// Sender = the requester. Receiver = anyone who can decide it now, its resolved approver, or whoever decided it.</summary>
public static class ApprovalFeedParticipants
{
    public static bool IsSender(WorkApprovalRequest r, Guid caller) => r.RequestedByEmployeeId == caller;

    public static bool IsReceiver(ProjectModuleTree tree, WorkApprovalRequest r, Guid caller)
        => caller != r.RequestedByEmployeeId
           && (r.ApproverEmployeeId == caller
               || r.DecidedByEmployeeId == caller
               || WorkApprovalDecisionRules.CanDecide(tree, r, caller));

    public static bool CanSee(ProjectModuleTree tree, WorkApprovalRequest r, Guid caller)
        => IsSender(r, caller) || IsReceiver(tree, r, caller);

    public static IReadOnlyCollection<Guid> NotifyTargets(WorkApprovalRequest r)
        => new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId, r.DecidedByEmployeeId ?? Guid.Empty }
            .Where(id => id != Guid.Empty).Distinct().ToList();
}
