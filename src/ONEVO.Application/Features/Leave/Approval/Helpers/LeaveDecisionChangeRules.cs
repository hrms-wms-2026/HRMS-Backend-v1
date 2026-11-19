using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Helpers;

/// <summary>
/// Who may change a final leave decision, and until when: only the approver whose approve/reject
/// made the request final, and only before the leave starts (after that, Cancel handles partial
/// days and attendance properly).
/// </summary>
public static class LeaveDecisionChangeRules
{
    /// <summary>The caller's row that decided the request, or null when the caller didn't decide it.</summary>
    public static LeaveRequestApprover? FindDecidingRow(LeaveApprovalState state, Guid employeeId)
    {
        var row = state.Approvers.SingleOrDefault(a => a.ApproverEmployeeId == employeeId);
        if (row is null)
            return null;

        return state.Request.Status switch
        {
            LeaveRequestStatuses.Approved when row.Status == LeaveRequestApproverStatuses.Approved &&
                                              state.Request.ApprovedBy == employeeId => row,
            LeaveRequestStatuses.Rejected when row.Status == LeaveRequestApproverStatuses.Rejected => row,
            _ => null
        };
    }

    public static bool HasStarted(LeaveApprovalState state, DateTimeOffset now) => now >= state.Request.StartAt;

    public static bool CanChange(LeaveApprovalState state, Guid employeeId, DateTimeOffset now) =>
        !HasStarted(state, now) && FindDecidingRow(state, employeeId) is not null;
}
