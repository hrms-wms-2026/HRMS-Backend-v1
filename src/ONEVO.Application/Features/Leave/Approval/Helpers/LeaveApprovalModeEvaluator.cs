using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Approval.Helpers;

public static class LeaveApprovalModeEvaluator
{
    public static ApprovalModeDecision ApplyApproval(
        string approvalMode,
        IReadOnlyList<ApprovalModeRow> rows,
        Guid currentApproverId)
    {
        if (approvalMode == LeaveApprovalModes.AnyOne)
        {
            var toSkip = rows
                .Where(row => row.ApproverEmployeeId != currentApproverId &&
                              row.Status == LeaveRequestApproverStatuses.Pending)
                .Select(row => row.ApproverEmployeeId)
                .ToList();

            return new ApprovalModeDecision(true, toSkip, []);
        }

        var remaining = rows
            .Where(row => row.Status == LeaveRequestApproverStatuses.Pending)
            .OrderBy(row => row.SequenceOrder)
            .ToList();

        if (remaining.Count == 0)
            return new ApprovalModeDecision(true, [], []);

        if (approvalMode == LeaveApprovalModes.InOrder)
        {
            var nextSequence = remaining.Min(row => row.SequenceOrder);
            return new ApprovalModeDecision(
                false,
                [],
                remaining.Where(row => row.SequenceOrder == nextSequence).Select(row => row.ApproverEmployeeId).ToList());
        }

        return new ApprovalModeDecision(false, [], remaining.Select(row => row.ApproverEmployeeId).ToList());
    }

    public static bool IsActionable(
        string approvalMode,
        IReadOnlyList<ApprovalModeRow> rows,
        Guid approverEmployeeId)
    {
        var row = rows.SingleOrDefault(x => x.ApproverEmployeeId == approverEmployeeId);
        if (row is null || row.Status != LeaveRequestApproverStatuses.Pending)
            return false;

        if (approvalMode != LeaveApprovalModes.InOrder)
            return true;

        var firstPendingSequence = rows
            .Where(x => x.Status == LeaveRequestApproverStatuses.Pending)
            .Min(x => x.SequenceOrder);

        return row.SequenceOrder == firstPendingSequence;
    }
}

public sealed record ApprovalModeRow(Guid ApproverEmployeeId, int SequenceOrder, string Status);

public sealed record ApprovalModeDecision(
    bool RequestCompleted,
    IReadOnlyList<Guid> ApproversToSkip,
    IReadOnlyList<Guid> NextApproverIds);
