using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>
/// Task 37: the inbox_items header of one leave request. Every command that changes a request or its approver rows
/// calls <see cref="StageAsync"/> before its own SaveChanges, so the header is saved in the same transaction.
/// "Must act now" uses <see cref="LeavePendingApprovals.IsActionable"/>, the rule the inbox and the pending-approvals
/// screen use, so the header can never disagree with them. Paths that skip this are found by InboxEntryConsistencyJob.
/// </summary>
public sealed class LeaveInboxEntrySync(IInboxEntryStore store, ILeaveApprovalRepository approvals)
{
    public Task StageAsync(LeaveApprovalState state, CancellationToken ct) => store.StageAsync(Build(state), ct);

    /// <summary>For paths that only change existing rows (cancel, revert): reloads the state from the same unit of work,
    /// whose tracked rows keep their unsaved changes. Rows added in this unit of work are not seen; pass the state instead.</summary>
    public async Task StageAsync(Guid tenantId, Guid requestId, CancellationToken ct)
    {
        if (await approvals.GetStateAsync(tenantId, requestId, ct) is { } state)
            await store.StageAsync(Build(state), ct);
    }

    public static InboxEntryState Build(LeaveApprovalState state)
    {
        var request = state.Request;
        var pending = request.Status == LeaveRequestStatuses.Pending;
        var decided = state.Approvers
            .Where(a => a.DecidedAt is not null
                && a.Status is LeaveRequestApproverStatuses.Approved or LeaveRequestApproverStatuses.Rejected)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

        var assignees = state.Approvers
            .GroupBy(a => a.ApproverEmployeeId)
            .Select(g =>
            {
                var rows = g.ToList();
                var status = pending && LeavePendingApprovals.IsActionable(state, g.Key)
                    ? InboxEntryAssigneeStatuses.Pending
                    : AssigneeStatus(Latest(rows).Status, pending);
                return new InboxEntryAssigneeState(g.Key, status, rows.Min(r => r.SequenceOrder));
            })
            .OrderBy(a => a.SequenceOrder).ThenBy(a => a.EmployeeId)
            .ToList();

        var lastActivity = new[] { request.CreatedAt, request.UpdatedAt ?? request.CreatedAt, decided?.DecidedAt ?? request.CreatedAt }.Max();
        var name = $"{state.Employee.FirstName} {state.Employee.LastName}".Trim();
        return new InboxEntryState(
            request.TenantId, LeaveInboxKinds.Source, LeaveInboxKinds.Request, request.Id, InboxEntryCategories.Request,
            SubjectEmployeeId: request.EmployeeId, RequestedByEmployeeId: request.EmployeeId,
            Status: HeaderStatus(request.Status), Title: state.LeaveTypeName,
            Summary: $"{name}, {request.StartAt:yyyy-MM-dd} to {request.EndAt:yyyy-MM-dd}",
            CreatedAt: request.CreatedAt, UpdatedAt: lastActivity,
            DecidedAt: pending ? null : request.ApprovedAt ?? decided?.DecidedAt,
            DecidedByEmployeeId: pending ? null : request.ApprovedBy ?? decided?.ApproverEmployeeId,
            Assignees: assignees);
    }

    private static LeaveRequestApprover Latest(
        List<LeaveRequestApprover> rows)
        => rows.OrderByDescending(r => r.DecidedAt ?? DateTimeOffset.MinValue).ThenByDescending(r => r.SequenceOrder).First();

    private static string HeaderStatus(string requestStatus) => requestStatus switch
    {
        LeaveRequestStatuses.Approved => InboxEntryStatuses.Approved,
        LeaveRequestStatuses.Rejected => InboxEntryStatuses.Rejected,
        LeaveRequestStatuses.Cancelled => InboxEntryStatuses.Cancelled,
        _ => InboxEntryStatuses.Pending,
    };

    /// <summary>An approver row that is not "act now": still pending means waiting for an earlier step, or, on a request
    /// that is no longer pending, a step that will never come.</summary>
    private static string AssigneeStatus(string approverStatus, bool requestPending) => approverStatus switch
    {
        LeaveRequestApproverStatuses.Pending => requestPending ? InboxEntryAssigneeStatuses.Waiting : InboxEntryAssigneeStatuses.Cancelled,
        LeaveRequestApproverStatuses.Approved => InboxEntryAssigneeStatuses.Approved,
        LeaveRequestApproverStatuses.Rejected => InboxEntryAssigneeStatuses.Rejected,
        LeaveRequestApproverStatuses.Skipped => InboxEntryAssigneeStatuses.Skipped,
        _ => InboxEntryAssigneeStatuses.Cancelled,
    };
}
