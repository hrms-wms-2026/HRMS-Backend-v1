using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Approval.Services;

/// <summary>
/// The one "leave requests this approver can act on now" query: ListPendingForApproverAsync, then the per-request
/// approval-mode check (in_order sequence, delegation already resolved into approver rows) that a repository
/// predicate cannot express. Shared by the pending-approvals screen, the My Team leave source and the Inbox, so
/// the three can never disagree.
/// </summary>
public static class LeavePendingApprovals
{
    public static async Task<IReadOnlyList<(LeavePendingApprovalListRow Row, LeaveApprovalState State)>> ListActionableAsync(
        ILeaveApprovalRepository repository, Guid tenantId, Guid approverEmployeeId, LeaveApprovalListFilter filter, CancellationToken ct)
    {
        var rows = await repository.ListPendingForApproverAsync(tenantId, approverEmployeeId, filter, ct);
        var actionable = new List<(LeavePendingApprovalListRow, LeaveApprovalState)>();
        foreach (var row in rows)
        {
            var state = await repository.GetStateAsync(tenantId, row.Request.Id, ct);
            if (state is not null && IsActionable(state, approverEmployeeId))
                actionable.Add((row, state));
        }
        return actionable;
    }

    public static bool IsActionable(LeaveApprovalState state, Guid approverEmployeeId)
        => state.ApprovalMode is not null
           && LeaveApprovalModeEvaluator.IsActionable(
               state.ApprovalMode,
               state.Approvers.Select(a => new ApprovalModeRow(a.ApproverEmployeeId, a.SequenceOrder, a.Status)).ToList(),
               approverEmployeeId);
}
