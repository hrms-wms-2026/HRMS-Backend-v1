using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>
/// The 30-minute undo of a leave decision (plan README §3.4), restoring exactly what the journal recorded before it:
/// approve - the request back to pending, the hours from used back to reserved, approver rows as they were, an
/// approver's date edit undone; blocked once the leave has started, the entitlement was recalculated or adjusted by
/// hand, or the request was (partly) cancelled since. Reject - back to pending with the hours reserved again; blocked
/// when the balance can no longer cover them.
/// </summary>
public sealed class LeaveDecisionReverter : InboxDecisionReverterBase
{
    public const string AlreadyStarted = "This leave has already started, so the approval can no longer be undone.";
    public const string BalanceChanged = "The leave balance was recalculated or adjusted after this decision, so it can no longer be undone.";
    public const string ChangedSince = "This request changed after your decision (for example it was cancelled), so it can no longer be undone.";
    public const string NotUndoable = "This kind of decision cannot be undone from the Inbox.";
    public const string UndoneTemplate = "leave_request_decision_undone";

    private readonly ILeaveApprovalRepository _approvals;
    private readonly LeaveRequestRangeEditor _editor;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationDispatcher _notifications;

    public LeaveDecisionReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ILeaveApprovalRepository approvals,
        LeaveRequestRangeEditor editor, ICurrentUser currentUser, INotificationDispatcher notifications)
        : base(journal, unitOfWork, clock)
    {
        _approvals = approvals;
        _editor = editor;
        _currentUser = currentUser;
        _clock = clock;
        _notifications = notifications;
    }

    public override string Source => LeaveInboxKinds.Source;
    public override string Kind => LeaveInboxKinds.Request;

    protected override async Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        // Undoing a decision is deciding again: the same permission the Leave approval endpoints require.
        if (decision.Action != LeaveInboxActions.Cancel && !_currentUser.HasPermission(LeaveInboxSourceProvider.LeaveApprovePermission))
            return Result.Forbidden(LeaveInboxSourceProvider.MissingPermission);
        var before = LeaveDecisionSnapshot.Parse(decision.BeforeJson);
        var after = LeaveDecisionSnapshot.Parse(decision.AfterJson);
        var state = await _approvals.GetStateAsync(tenantId, decision.ItemId, ct);
        if (before is null || after is null || state is null)
            return Result.UnprocessableEntity(NotUndoable);
        if (state.Request.Status != after.Status || state.Request.PartialCancelEffectiveDate is not null)
            return Result.Conflict(ChangedSince);

        switch (decision.Action)
        {
            case LeaveInboxActions.Approve:
                if (_clock.UtcNow >= state.Request.StartAt)
                    return Result.Conflict(AlreadyStarted);
                var audits = await _approvals.ListBalanceAuditsSinceAsync(
                    tenantId, state.Request.EmployeeId, state.Request.LeaveTypeId, decision.DecidedAt, ct);
                // Other requests' deductions are fine; an accrual, carry-forward, forfeiture or a manual adjustment
                // (no related request) means the entitlement was recalculated under this decision.
                if (audits.Any(a => a.ChangeType is LeaveBalanceChangeTypes.Accrual or LeaveBalanceChangeTypes.CarryForward
                        or LeaveBalanceChangeTypes.Forfeiture
                    || (a.ChangeType == LeaveBalanceChangeTypes.Adjustment && a.RelatedRequestId is null)))
                    return Result.Conflict(BalanceChanged);
                return Result.Success();
            case LeaveInboxActions.Reject:
                if (state.Entitlement is null)
                    return Result.Conflict(BalanceChanged);
                var remaining = LeaveApprovalMapper.CalculateRemaining(
                    state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours, state.Entitlement.UsedHours, state.Entitlement.PendingHours);
                return state.Request.PaidHours > 0m && remaining < state.Request.PaidHours
                    ? Result.Conflict($"The balance can no longer cover this request ({remaining:0.##} h left).")
                    : Result.Success();
            default:
                return Result.UnprocessableEntity(NotUndoable);
        }
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var before = LeaveDecisionSnapshot.Parse(decision.BeforeJson)!;
        var after = LeaveDecisionSnapshot.Parse(decision.AfterJson)!;
        var state = (await _approvals.GetStateAsync(tenantId, decision.ItemId, ct))!;
        var now = _clock.UtcNow;

        // Approver rows back as they were; a row the decision added (forward) is removed.
        foreach (var row in state.Approvers.ToList())
        {
            if (before.Approvers.FirstOrDefault(b => b.Id == row.Id) is { } was)
            {
                row.Status = was.Status;
                row.Comment = was.Comment;
                row.DecidedAt = was.DecidedAt;
            }
            else if (AddedRows(before, after).Contains(row.Id))
            {
                _approvals.RemoveApprover(row);
            }
        }

        var request = state.Request;
        var wasCompleted = after.Status != before.Status;
        request.Status = before.Status;
        request.ApprovedBy = before.ApprovedBy;
        request.ApprovedAt = before.ApprovedAt;
        request.UpdatedAt = now;

        if (state.Entitlement is { } entitlement && wasCompleted && request.PaidHours > 0m)
        {
            if (after.Status == LeaveRequestStatuses.Approved)
            {
                entitlement.UsedHours -= request.PaidHours;
                entitlement.PendingHours += request.PaidHours;
            }
            else if (after.Status == LeaveRequestStatuses.Rejected)
            {
                entitlement.PendingHours += request.PaidHours;
            }
            entitlement.UpdatedAt = now;
            await _approvals.AddBalanceAuditAsync(new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, LeaveTypeId = request.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Adjustment,
                HoursChanged = after.Status == LeaveRequestStatuses.Approved ? request.PaidHours : 0m,
                BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
                    entitlement.TotalHours, entitlement.CarriedForwardHours, entitlement.UsedHours, entitlement.PendingHours),
                Reason = $"Decision undone in the Inbox ({decision.Action})",
                RelatedRequestId = request.Id, CreatedAt = now, CreatedBy = _currentUser.UserId,
            }, ct);
        }

        // An approval that also moved the dates: move them back (re-runs the leave rules for the original range).
        if (decision.Action == LeaveInboxActions.Approve && (before.Range.StartAt != after.Range.StartAt || before.Range.EndAt != after.Range.EndAt))
        {
            var moved = await _editor.ApplyAsync(state, before.Range.StartAt, before.Range.EndAt, "Date change undone in the Inbox", ct,
                allowLonger: true);
            if (!moved.IsSuccess)
                return Result<InboxActionResult>.Failure(moved.Error!, moved.StatusCode ?? 409);
        }

        // The employee was told "approved" / "rejected": tell them it is pending again (the bell lands in the same save).
        if (wasCompleted && state.Employee.UserId != Guid.Empty)
            await _notifications.SendTemplatedAsync(tenantId, state.Employee.UserId, UndoneTemplate,
                new Dictionary<string, string>
                {
                    ["leaveType"] = state.LeaveTypeName,
                    ["startDate"] = request.StartAt.ToString("dd MMM yyyy"),
                    ["decision"] = after.Status == LeaveRequestStatuses.Approved ? "approval" : "rejection",
                },
                InboxNotificationNavigation.RelatedEntityType(LeaveInboxKinds.Source, LeaveInboxKinds.Request), request.Id, ct);

        await _approvals.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult(LeaveInboxSourceProvider.People(state)));
    }

    private static IEnumerable<Guid> AddedRows(LeaveDecisionSnapshot before, LeaveDecisionSnapshot after)
        => after.Approvers.Select(a => a.Id).Except(before.Approvers.Select(b => b.Id));
}
