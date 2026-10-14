using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Commands;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// The 30-minute undo of an attendance alert resolution (plan E2): the alert goes back to open. A conversion to leave
/// also cancels the created leave through the Leave module's own cancel command (its approver-created-leave path,
/// reason "Reverted from Inbox"), which restores the balance. Blocked when the alert or that leave changed since.
/// </summary>
public sealed class AttendanceAlertReverter : InboxDecisionReverterBase
{
    public const string ChangedSince = "This alert changed after your decision, so it can no longer be undone.";
    public const string LeaveChangedSince = "The leave created from this alert changed after your decision, so it can no longer be undone.";
    public const string RevertReason = "Reverted from Inbox";

    private readonly IAttendanceAlertRepository _alerts;
    private readonly ILeaveApprovalRepository _leaveApprovals;
    private readonly AttendanceAlertInbox _inbox;
    private readonly ISender _sender;
    private readonly IDateTimeProvider _clock;

    public AttendanceAlertReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, IAttendanceAlertRepository alerts,
        ILeaveApprovalRepository leaveApprovals, AttendanceAlertInbox inbox, ISender sender)
        : base(journal, unitOfWork, clock)
    {
        _alerts = alerts;
        _leaveApprovals = leaveApprovals;
        _inbox = inbox;
        _sender = sender;
        _clock = clock;
    }

    public override string Source => AttendanceInboxKinds.Source;
    public override string Kind => AttendanceInboxKinds.Alert;

    protected override async Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        if (!decision.Action.StartsWith(InboxJournalActions.ResolvePrefix, StringComparison.Ordinal))
            return Result.UnprocessableEntity(AttendanceDecisionReverterBase.NotUndoable);
        var alert = await _alerts.GetByIdAsync(tenantId, decision.ItemId, ct);
        var after = AttendanceAlertSnapshot.Parse(decision.AfterJson);
        if (alert is null || after is null || AttendanceAlertSnapshot.Parse(decision.BeforeJson) is null)
            return Result.NotFound("Attendance alert not found.");
        if (alert.Status != after.Status || alert.Outcome != after.Outcome || alert.ConvertedLeaveRequestId != after.ConvertedLeaveRequestId)
            return Result.Conflict(ChangedSince);
        if (!await _inbox.CanReviewAsync(alert, decision.ActorEmployeeId, ct))
            return Result.Forbidden(AttendanceInboxSourceProvider.MissingPermission);
        if (after.ConvertedLeaveRequestId is Guid leaveId)
        {
            var leave = (await _leaveApprovals.GetStateAsync(tenantId, leaveId, ct))?.Request;
            if (leave is null || leave.Status != LeaveRequestStatuses.Approved || leave.UpdatedAt != after.LeaveUpdatedAt)
                return Result.Conflict(LeaveChangedSince);
        }
        return Result.Success();
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var alert = (await _alerts.GetByIdAsync(tenantId, decision.ItemId, ct))!;
        if (alert.ConvertedLeaveRequestId is Guid leaveId)
        {
            var cancelled = await _sender.Send(new CancelLeaveRequestCommand(leaveId, RevertReason, null, null, RevertOfApproverBackdatedLeave: true), ct);
            if (!cancelled.IsSuccess)
                return Result<InboxActionResult>.Failure(cancelled.Error ?? "The leave could not be cancelled.", cancelled.StatusCode ?? 409);
        }
        var before = AttendanceAlertSnapshot.Parse(decision.BeforeJson)!;
        alert.Status = before.Status;
        alert.Outcome = before.Outcome;
        alert.Note = before.Note;
        alert.ResolvedByEmployeeId = before.ResolvedByEmployeeId;
        alert.ResolvedAt = before.ResolvedAt;
        alert.ConvertedLeaveRequestId = before.ConvertedLeaveRequestId;
        alert.UpdatedAt = _clock.UtcNow;
        await _alerts.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([alert.EmployeeId, callerEmployeeId]));
    }
}
