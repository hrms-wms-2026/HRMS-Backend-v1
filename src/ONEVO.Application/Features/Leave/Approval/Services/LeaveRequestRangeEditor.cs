using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Request.Mappers;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Approval.Services;

/// <summary>The request's range and hours before and after an edit.</summary>
public sealed record LeaveRangeSnapshot(DateTimeOffset StartAt, DateTimeOffset EndAt, decimal TotalHours, decimal PaidHours, decimal UnpaidHours);

/// <summary>
/// Moves a pending leave request to new dates for an approver (edit before approve, plan L1 / P3). The full submission
/// rules (balance, overlap, blackout, policy, type) are re-run for the new range through
/// <see cref="LeaveRequestSubmissionEvaluator"/>, leaving the request itself out of the overlap check and counting its
/// own reserved hours as available. On success the request, its day allocations and the reserved (pending) hours are
/// rewritten and the change is recorded as a balance-audit adjustment. Nothing is saved here.
/// </summary>
public sealed class LeaveRequestRangeEditor
{
    public const string OtherYear = "Leave cannot be moved into another year.";
    public const string NotPending = "Only a pending request can be edited.";
    public const string CannotLengthen = "An approver can shorten or move a request, but not make it longer.";

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILeaveApprovalRepository _repository;
    private readonly LeaveRequestSubmissionEvaluator _evaluator;
    private readonly LeaveRequestDayAllocationBuilder _allocations;

    public LeaveRequestRangeEditor(
        ICurrentUser currentUser, IDateTimeProvider clock, ILeaveApprovalRepository repository,
        LeaveRequestSubmissionEvaluator evaluator, LeaveRequestDayAllocationBuilder allocations)
    {
        _currentUser = currentUser;
        _clock = clock;
        _repository = repository;
        _evaluator = evaluator;
        _allocations = allocations;
    }

    public async Task<Result<(LeaveRangeSnapshot Before, LeaveRangeSnapshot After)>> ApplyAsync(
        LeaveApprovalState state, DateTimeOffset startAt, DateTimeOffset endAt, string auditReason, CancellationToken ct,
        bool allowLonger = false)
    {
        var request = state.Request;
        if (request.Status != LeaveRequestStatuses.Pending)
            return Result<(LeaveRangeSnapshot, LeaveRangeSnapshot)>.Conflict(NotPending);
        if (startAt.Year != request.StartAt.Year)
            return Result<(LeaveRangeSnapshot, LeaveRangeSnapshot)>.UnprocessableEntity(OtherYear);

        var before = Snapshot(request);
        var documents = await _repository.CountDocumentsAsync(_currentUser.TenantId, request.Id, ct);
        var evaluation = await _evaluator.EvaluateAsync(
            _currentUser.TenantId, _currentUser.UserId, request.EmployeeId, request.LeaveTypeId, startAt, endAt, request.Reason, [], ct,
            new LeaveEvaluationOverrides(ExcludeRequestId: request.Id, ReleasedPendingHours: request.PaidHours, DocumentsOnFile: documents));
        if (!evaluation.IsSuccess)
            return Result<(LeaveRangeSnapshot, LeaveRangeSnapshot)>.UnprocessableEntity(evaluation.Error!);

        var draft = evaluation.Value!;
        // An approver may not book more leave than the employee asked for (P3: shorten or shift). Only an undo
        // that restores the employee's own original range may be longer than the current one.
        if (!allowLonger && draft.TotalHours > before.TotalHours)
            return Result<(LeaveRangeSnapshot, LeaveRangeSnapshot)>.UnprocessableEntity(CannotLengthen);
        var now = _clock.UtcNow;
        var entitlement = state.Entitlement ?? draft.Entitlement;
        entitlement.PendingHours += draft.PaidHours - request.PaidHours;
        entitlement.UpdatedAt = now;

        request.StartAt = startAt;
        request.EndAt = endAt;
        request.TotalHours = draft.TotalHours;
        request.PaidHours = draft.PaidHours;
        request.UnpaidHours = draft.UnpaidHours;
        request.NoticePeriodMissed = draft.NoticePeriodMissed;
        request.ConflictSnapshotJson = LeaveRequestMapper.ToConflictSnapshotJson(draft.Warnings, draft.CalendarConflicts, draft.TeamAbsencePercent);
        request.UpdatedAt = now;

        foreach (var allocation in await _repository.ListActiveAllocationsAsync(_currentUser.TenantId, request.Id, ct))
        {
            allocation.Status = LeaveRequestDayAllocationStatuses.Cancelled;
            allocation.CancelledAt = now;
        }
        await _repository.AddAllocationsAsync(_allocations.ToEntities(_currentUser.TenantId, request.Id,
            _allocations.Build(draft.CountedDates, draft.HoursByDate, draft.PaidHours, draft.UnpaidHours), now), ct);

        await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            ChangeType = LeaveBalanceChangeTypes.Adjustment,
            HoursChanged = before.PaidHours - draft.PaidHours,
            BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
                entitlement.TotalHours, entitlement.CarriedForwardHours, entitlement.UsedHours, entitlement.PendingHours),
            Reason = auditReason,
            RelatedRequestId = request.Id,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        }, ct);

        return Result<(LeaveRangeSnapshot, LeaveRangeSnapshot)>.Success((before, Snapshot(request)));
    }

    public static LeaveRangeSnapshot Snapshot(ONEVO.Domain.Features.Leave.Request.Entities.LeaveRequest request)
        => new(request.StartAt, request.EndAt, request.TotalHours, request.PaidHours, request.UnpaidHours);

    public static string Describe(LeaveRangeSnapshot range)
        => $"{range.StartAt:yyyy-MM-dd HH:mm} - {range.EndAt:yyyy-MM-dd HH:mm} ({range.TotalHours:0.##} h)";
}
