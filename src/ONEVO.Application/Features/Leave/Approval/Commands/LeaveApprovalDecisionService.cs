using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.Options;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed class LeaveApprovalDecisionService
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveApprovalRepository _repository;
    private readonly IOutboxWriter _outbox;
    private readonly INotificationDispatcher _notifications;
    private readonly ILeaveRequestConflictProvider _conflicts;
    private readonly LeaveForwardTargetResolver _forwardTargets;
    private readonly LeaveApprovalOptions _options;

    public LeaveApprovalDecisionService(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IEmployeeRepository employees,
        ILeaveApprovalRepository repository,
        IOutboxWriter outbox,
        INotificationDispatcher notifications,
        ILeaveRequestConflictProvider conflicts,
        LeaveForwardTargetResolver forwardTargets,
        IOptions<LeaveApprovalOptions> options)
    {
        _currentUser = currentUser;
        _clock = clock;
        _employees = employees;
        _repository = repository;
        _outbox = outbox;
        _notifications = notifications;
        _conflicts = conflicts;
        _forwardTargets = forwardTargets;
        _options = options.Value;
    }

    public Task<Result<LeaveApprovalDecisionResponse>> ApproveAsync(Guid requestId, string? comment, CancellationToken ct) =>
        DecideAsync(requestId, comment, ct);

    public async Task<Result<LeaveApprovalDecisionResponse>> RejectAsync(Guid requestId, string reason, CancellationToken ct)
    {
        var loaded = await LoadAsync(requestId, requireActionableApprover: true, ct);
        if (!loaded.IsSuccess)
            return Result<LeaveApprovalDecisionResponse>.Failure(loaded.Error!, loaded.StatusCode ?? 400);

        var (state, currentEmployee, approverRow) = loaded.Value!;
        if (!_options.AllowSelfApproval && state.Request.EmployeeId == currentEmployee.Id)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.SelfApproval);

        approverRow!.Status = LeaveRequestApproverStatuses.Rejected;
        approverRow.Comment = reason.Trim();
        approverRow.DecidedAt = _clock.UtcNow;
        state.Request.Status = LeaveRequestStatuses.Rejected;
        state.Request.UpdatedAt = _clock.UtcNow;

        foreach (var pending in state.Approvers.Where(row => row.Status == LeaveRequestApproverStatuses.Pending))
        {
            pending.Status = LeaveRequestApproverStatuses.Skipped;
            pending.DecidedAt = _clock.UtcNow;
        }

        if (state.Entitlement is not null)
        {
            state.Entitlement.PendingHours -= state.Request.PaidHours;
            state.Entitlement.UpdatedAt = _clock.UtcNow;
        }

        await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestRejected, new LeaveRequestRejectedPayload(
            _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
            state.Request.StartAt, state.Request.EndAt, state.Request.PaidHours, state.Request.UnpaidHours,
            currentEmployee.Id, reason.Trim()), _currentUser.TenantId, ct);

        await NotifyEmployeeAsync(state, "leave_request_rejected", new Dictionary<string, string>
        {
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd"),
            ["reason"] = reason.Trim()
        }, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
    }

    public async Task<Result<LeaveApprovalDecisionResponse>> RequestInfoAsync(Guid requestId, string question, CancellationToken ct)
    {
        var loaded = await LoadAsync(requestId, requireActionableApprover: true, ct);
        if (!loaded.IsSuccess)
            return Result<LeaveApprovalDecisionResponse>.Failure(loaded.Error!, loaded.StatusCode ?? 400);

        var (state, currentEmployee, approverRow) = loaded.Value!;
        state.Request.Status = LeaveRequestStatuses.InformationRequested;
        state.Request.UpdatedAt = _clock.UtcNow;
        approverRow!.Status = LeaveRequestApproverStatuses.InformationRequested;
        approverRow.Comment = question.Trim();
        approverRow.DecidedAt = null;

        await _repository.AddInfoMessageAsync(new LeaveRequestInfoMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LeaveRequestId = state.Request.Id,
            SenderEmployeeId = currentEmployee.Id,
            Message = question.Trim(),
            CreatedAt = _clock.UtcNow
        }, ct);

        await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveInformationRequested, new LeaveInformationRequestedPayload(
            _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
            state.Request.StartAt, state.Request.EndAt, currentEmployee.Id, question.Trim()), _currentUser.TenantId, ct);

        var approverName = LeaveEntitlementName(currentEmployee);
        await NotifyEmployeeAsync(state, "leave_request_information_requested", new Dictionary<string, string>
        {
            ["approverName"] = approverName,
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd")
        }, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
    }

    /// <summary>
    /// Hands the request up to the approver's own manager (see LeaveForwardTargetResolver): the
    /// caller's row becomes forwarded and the target takes over the same slot as a pending approver.
    /// The request itself stays pending, so approval-mode evaluation is unchanged.
    /// </summary>
    public async Task<Result<LeaveApprovalDecisionResponse>> ForwardAsync(Guid requestId, string? note, CancellationToken ct)
    {
        var loaded = await LoadAsync(requestId, requireActionableApprover: true, ct);
        if (!loaded.IsSuccess)
            return Result<LeaveApprovalDecisionResponse>.Failure(loaded.Error!, loaded.StatusCode ?? 400);

        var (state, currentEmployee, approverRow) = loaded.Value!;
        var targetId = await _forwardTargets.ResolveAsync(_currentUser.TenantId, state, currentEmployee.Id, ct);
        if (targetId is null)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.NoForwardTarget);

        approverRow!.Status = LeaveRequestApproverStatuses.Forwarded;
        approverRow.Comment = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        approverRow.DecidedAt = _clock.UtcNow;
        state.Request.UpdatedAt = _clock.UtcNow;

        await _repository.AddApproverAsync(new LeaveRequestApprover
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LeaveRequestId = state.Request.Id,
            ApproverEmployeeId = targetId.Value,
            SequenceOrder = approverRow.SequenceOrder,
            Status = LeaveRequestApproverStatuses.Pending
        }, ct);

        await NotifyApproverAsync(targetId.Value, state, ct);
        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
    }

    /// <summary>
    /// Lets the approver who made a request final flip their decision before the leave starts.
    /// approved -> rejected gives the used paid hours back; rejected -> approved re-checks the balance
    /// and re-runs the approval mode (approvers skipped by the rejection come back as pending).
    /// </summary>
    public async Task<Result<LeaveApprovalDecisionResponse>> ChangeDecisionAsync(
        Guid requestId, string decision, string? comment, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.TenantMissing);

        var currentEmployee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (currentEmployee is null)
            return Result<LeaveApprovalDecisionResponse>.NotFound(LeaveApprovalMessages.NoEmployee);

        var state = await _repository.GetStateAsync(_currentUser.TenantId, requestId, ct);
        if (state is null)
            return Result<LeaveApprovalDecisionResponse>.NotFound(LeaveApprovalMessages.NotFound);

        if (state.Request.Status is not (LeaveRequestStatuses.Approved or LeaveRequestStatuses.Rejected))
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.NoFinalDecision);

        var now = _clock.UtcNow;
        if (LeaveDecisionChangeRules.HasStarted(state, now))
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.LeaveAlreadyStarted);

        var row = LeaveDecisionChangeRules.FindDecidingRow(state, currentEmployee.Id);
        if (row is null)
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.NotDecider);

        var wantsApprove = decision == ChangeLeaveDecisionValues.Approve;
        if (wantsApprove == (state.Request.Status == LeaveRequestStatuses.Approved))
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.SameDecision);

        return wantsApprove
            ? await ChangeToApprovedAsync(state, row, currentEmployee, comment, now, ct)
            : await ChangeToRejectedAsync(state, row, currentEmployee, comment!.Trim(), now, ct);
    }

    private async Task<Result<LeaveApprovalDecisionResponse>> ChangeToRejectedAsync(
        LeaveApprovalState state, LeaveRequestApprover row, Employee currentEmployee, string reason, DateTimeOffset now, CancellationToken ct)
    {
        row.Status = LeaveRequestApproverStatuses.Rejected;
        row.Comment = $"Changed from approved: {reason}";
        row.DecidedAt = now;
        state.Request.Status = LeaveRequestStatuses.Rejected;
        state.Request.ApprovedBy = null;
        state.Request.ApprovedAt = null;
        state.Request.UpdatedAt = now;

        if (state.Entitlement is not null && state.Request.PaidHours > 0m)
        {
            state.Entitlement.UsedHours = Math.Max(0m, state.Entitlement.UsedHours - state.Request.PaidHours);
            state.Entitlement.UpdatedAt = now;
            await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId,
                EmployeeId = state.Request.EmployeeId,
                LeaveTypeId = state.Request.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Adjustment,
                HoursChanged = state.Request.PaidHours,
                BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
                    state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
                    state.Entitlement.UsedHours, state.Entitlement.PendingHours),
                Reason = "Decision changed: approved -> rejected",
                RelatedRequestId = state.Request.Id,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            }, ct);
        }

        await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestRejected, new LeaveRequestRejectedPayload(
            _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
            state.Request.StartAt, state.Request.EndAt, state.Request.PaidHours, state.Request.UnpaidHours,
            currentEmployee.Id, reason), _currentUser.TenantId, ct);

        await NotifyEmployeeAsync(state, "leave_request_rejected", new Dictionary<string, string>
        {
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd"),
            ["reason"] = reason
        }, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
    }

    private async Task<Result<LeaveApprovalDecisionResponse>> ChangeToApprovedAsync(
        LeaveApprovalState state, LeaveRequestApprover row, Employee currentEmployee, string? comment, DateTimeOffset now, CancellationToken ct)
    {
        if (!_options.AllowSelfApproval && state.Request.EmployeeId == currentEmployee.Id)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.SelfApproval);
        if (state.ApprovalMode is null)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.MissingPolicy);
        if (state.Entitlement is null)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.BalanceChanged(0m));

        // The rejection released the reserved hours, so they must fit in the balance again.
        var remaining = LeaveApprovalMapper.CalculateRemaining(
            state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
            state.Entitlement.UsedHours, state.Entitlement.PendingHours);
        if (state.Request.PaidHours > 0m && remaining < state.Request.PaidHours)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.BalanceChanged(remaining));

        row.Status = LeaveRequestApproverStatuses.Approved;
        row.Comment = string.IsNullOrWhiteSpace(comment) ? "Changed from rejected" : $"Changed from rejected: {comment.Trim()}";
        row.DecidedAt = now;

        // A rejection skips every other pending approver. Under any_one this approval completes the
        // request anyway; under the other modes those approvers still have to decide.
        if (state.ApprovalMode != LeaveApprovalModes.AnyOne)
        {
            foreach (var skipped in state.Approvers.Where(a => a.Status == LeaveRequestApproverStatuses.Skipped))
            {
                skipped.Status = LeaveRequestApproverStatuses.Pending;
                skipped.DecidedAt = null;
            }
        }

        var rows = state.Approvers
            .Select(a => new ApprovalModeRow(a.ApproverEmployeeId, a.SequenceOrder, a.Status))
            .ToList();
        var decision = LeaveApprovalModeEvaluator.ApplyApproval(state.ApprovalMode, rows, currentEmployee.Id);
        foreach (var skippedId in decision.ApproversToSkip)
        {
            var skipped = state.Approvers.Single(a => a.ApproverEmployeeId == skippedId);
            skipped.Status = LeaveRequestApproverStatuses.Skipped;
            skipped.DecidedAt = now;
        }

        state.Request.UpdatedAt = now;
        state.Entitlement.UpdatedAt = now;
        var paidMoved = 0m;
        if (decision.RequestCompleted)
        {
            state.Request.Status = LeaveRequestStatuses.Approved;
            state.Request.ApprovedBy = currentEmployee.Id;
            state.Request.ApprovedAt = now;
            state.Entitlement.UsedHours += state.Request.PaidHours;
            paidMoved = state.Request.PaidHours;

            if (state.Request.PaidHours > 0m)
            {
                await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
                {
                    Id = Guid.NewGuid(),
                    TenantId = _currentUser.TenantId,
                    EmployeeId = state.Request.EmployeeId,
                    LeaveTypeId = state.Request.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Deduction,
                    HoursChanged = -state.Request.PaidHours,
                    BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
                        state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
                        state.Entitlement.UsedHours, state.Entitlement.PendingHours),
                    Reason = "Decision changed: rejected -> approved",
                    RelatedRequestId = state.Request.Id,
                    CreatedAt = now,
                    CreatedBy = _currentUser.UserId
                }, ct);
            }

            await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestApproved, new LeaveRequestApprovedPayload(
                _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
                state.Request.StartAt, state.Request.EndAt, state.Request.PaidHours, state.Request.UnpaidHours,
                currentEmployee.Id), _currentUser.TenantId, ct);

            await NotifyEmployeeAsync(state, "leave_request_approved", new Dictionary<string, string>
            {
                ["leaveTypeName"] = state.LeaveTypeName,
                ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
                ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd")
            }, ct);
        }
        else
        {
            // Back in the approval flow: reserve the hours again until the others decide.
            state.Request.Status = LeaveRequestStatuses.Pending;
            state.Entitlement.PendingHours += state.Request.PaidHours;
            foreach (var nextId in decision.NextApproverIds)
                await NotifyApproverAsync(nextId, state, ct);
        }

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, paidMoved, ct));
    }

    public async Task<Result<LeaveApprovalDecisionResponse>> RespondInfoAsync(
        Guid requestId, string message, IReadOnlyList<Guid> fileRecordIds, CancellationToken ct)
    {
        var loaded = await LoadAsync(requestId, requireActionableApprover: false, ct);
        if (!loaded.IsSuccess)
            return Result<LeaveApprovalDecisionResponse>.Failure(loaded.Error!, loaded.StatusCode ?? 400);

        var (state, currentEmployee, _) = loaded.Value!;
        if (state.Request.EmployeeId != currentEmployee.Id)
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.NotYours);
        if (state.Request.Status != LeaveRequestStatuses.InformationRequested)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.NotWaitingInfo);

        var paused = state.Approvers.SingleOrDefault(row => row.Status == LeaveRequestApproverStatuses.InformationRequested);
        if (paused is null)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.NoPausedApprover);

        if (!await _repository.AreAvailableFileRecordsAsync(_currentUser.TenantId, fileRecordIds, ct))
            return Result<LeaveApprovalDecisionResponse>.Failure(LeaveApprovalMessages.FileNotAvailable);

        if (fileRecordIds.Count > 0)
        {
            await _repository.AddDocumentsAsync(fileRecordIds.Distinct().Select(id => new LeaveRequestDocument
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId,
                LeaveRequestId = state.Request.Id,
                FileRecordId = id
            }).ToList(), ct);
        }

        state.Request.Status = LeaveRequestStatuses.Pending;
        state.Request.UpdatedAt = _clock.UtcNow;
        paused.Status = LeaveRequestApproverStatuses.Pending;

        await _repository.AddInfoMessageAsync(new LeaveRequestInfoMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LeaveRequestId = state.Request.Id,
            SenderEmployeeId = currentEmployee.Id,
            Message = message.Trim(),
            CreatedAt = _clock.UtcNow
        }, ct);

        await NotifyApproverAsync(paused.ApproverEmployeeId, state, ct);
        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
    }

    private async Task<Result<LeaveApprovalDecisionResponse>> DecideAsync(Guid requestId, string? comment, CancellationToken ct)
    {
        var loaded = await LoadAsync(requestId, requireActionableApprover: true, ct);
        if (!loaded.IsSuccess)
            return Result<LeaveApprovalDecisionResponse>.Failure(loaded.Error!, loaded.StatusCode ?? 400);

        var (state, currentEmployee, approverRow) = loaded.Value!;
        if (!_options.AllowSelfApproval && state.Request.EmployeeId == currentEmployee.Id)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.SelfApproval);

        if (state.Entitlement is null)
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.BalanceChanged(0m));

        var remainingBefore = LeaveApprovalMapper.CalculateRemaining(
            state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
            state.Entitlement.UsedHours, state.Entitlement.PendingHours);
        if (state.Request.PaidHours > 0m &&
            (state.Entitlement.PendingHours < state.Request.PaidHours || remainingBefore < 0m))
        {
            return Result<LeaveApprovalDecisionResponse>.Conflict(LeaveApprovalMessages.BalanceChanged(remainingBefore));
        }

        approverRow!.Status = LeaveRequestApproverStatuses.Approved;
        approverRow.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        approverRow.DecidedAt = _clock.UtcNow;

        var rows = state.Approvers
            .Select(row => new ApprovalModeRow(row.ApproverEmployeeId, row.SequenceOrder, row.Status))
            .ToList();
        var decision = LeaveApprovalModeEvaluator.ApplyApproval(state.ApprovalMode!, rows, currentEmployee.Id);

        foreach (var skippedId in decision.ApproversToSkip)
        {
            var skipped = state.Approvers.Single(row => row.ApproverEmployeeId == skippedId);
            skipped.Status = LeaveRequestApproverStatuses.Skipped;
            skipped.DecidedAt = _clock.UtcNow;
        }

        var paidMoved = 0m;
        if (decision.RequestCompleted)
        {
            state.Request.Status = LeaveRequestStatuses.Approved;
            state.Request.ApprovedBy = currentEmployee.Id;
            state.Request.ApprovedAt = _clock.UtcNow;
            state.Request.UpdatedAt = _clock.UtcNow;
            paidMoved = state.Request.PaidHours;
            state.Entitlement.PendingHours -= state.Request.PaidHours;
            state.Entitlement.UsedHours += state.Request.PaidHours;
            state.Entitlement.UpdatedAt = _clock.UtcNow;

            var balanceAfter = LeaveApprovalMapper.CalculateRemaining(
                state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
                state.Entitlement.UsedHours, state.Entitlement.PendingHours);
            if (state.Request.PaidHours > 0m)
            {
                await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
                {
                    Id = Guid.NewGuid(),
                    TenantId = _currentUser.TenantId,
                    EmployeeId = state.Request.EmployeeId,
                    LeaveTypeId = state.Request.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Deduction,
                    HoursChanged = -state.Request.PaidHours,
                    BalanceAfter = balanceAfter,
                    Reason = "Leave request approved",
                    RelatedRequestId = state.Request.Id,
                    CreatedAt = _clock.UtcNow,
                    CreatedBy = _currentUser.UserId
                }, ct);
            }

            await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestApproved, new LeaveRequestApprovedPayload(
                _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
                state.Request.StartAt, state.Request.EndAt, state.Request.PaidHours, state.Request.UnpaidHours,
                currentEmployee.Id), _currentUser.TenantId, ct);

            await NotifyEmployeeAsync(state, "leave_request_approved", new Dictionary<string, string>
            {
                ["leaveTypeName"] = state.LeaveTypeName,
                ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
                ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd")
            }, ct);
        }
        else
        {
            foreach (var nextId in decision.NextApproverIds)
                await NotifyApproverAsync(nextId, state, ct);
        }

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, paidMoved, ct));
    }

    private async Task<Result<(LeaveApprovalState State, Employee CurrentEmployee, LeaveRequestApprover? Approver)>> LoadAsync(
        Guid requestId, bool requireActionableApprover, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (_currentUser.TenantId == Guid.Empty)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Forbidden(LeaveApprovalMessages.TenantMissing);

        var currentEmployee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (currentEmployee is null)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.NotFound(LeaveApprovalMessages.NoEmployee);

        var state = await _repository.GetStateAsync(_currentUser.TenantId, requestId, ct);
        if (state is null)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.NotFound(LeaveApprovalMessages.NotFound);

        if (state.Request.Status is LeaveRequestStatuses.Approved or LeaveRequestStatuses.Rejected or LeaveRequestStatuses.Cancelled)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Conflict(LeaveApprovalMessages.AlreadyFinal);

        if (state.ApprovalMode is null)
            return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Conflict(LeaveApprovalMessages.MissingPolicy);

        LeaveRequestApprover? approver = null;
        if (requireActionableApprover)
        {
            var modeRows = state.Approvers.Select(x => new ApprovalModeRow(x.ApproverEmployeeId, x.SequenceOrder, x.Status)).ToList();
            if (!LeaveApprovalModeEvaluator.IsActionable(state.ApprovalMode, modeRows, currentEmployee.Id))
                return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Forbidden(LeaveApprovalMessages.NotAssigned);
            approver = state.Approvers.Single(x => x.ApproverEmployeeId == currentEmployee.Id);
        }

        return Result<(LeaveApprovalState, Employee, LeaveRequestApprover?)>.Success((state, currentEmployee, approver));
    }

    private async Task<LeaveApprovalDecisionResponse> MapAsync(LeaveApprovalState state, decimal paidMoved, CancellationToken ct)
    {
        var remaining = state.Entitlement is null
            ? 0m
            : LeaveApprovalMapper.CalculateRemaining(
                state.Entitlement.TotalHours, state.Entitlement.CarriedForwardHours,
                state.Entitlement.UsedHours, state.Entitlement.PendingHours);
        var warnings = await CurrentWarningsAsync(state, ct);
        var currentState = state.Approvers
            .OrderBy(x => x.SequenceOrder)
            .Select(x => x.Status)
            .FirstOrDefault(status => status is LeaveRequestApproverStatuses.Pending or LeaveRequestApproverStatuses.InformationRequested)
            ?? state.Request.Status;
        return LeaveApprovalMapper.ToDecision(state.Request, paidMoved, remaining, currentState, warnings);
    }

    private async Task<IReadOnlyList<LeaveApprovalWarningResponse>> CurrentWarningsAsync(LeaveApprovalState state, CancellationToken ct)
    {
        var conflicts = await _conflicts.ListConflictsAsync(
            _currentUser.TenantId,
            state.Request.EmployeeId,
            DateOnly.FromDateTime(state.Request.StartAt.UtcDateTime),
            DateOnly.FromDateTime(state.Request.EndAt.UtcDateTime),
            ct);
        return conflicts.Select(c => new LeaveApprovalWarningResponse("current_conflict", c.Title)).ToList();
    }

    private async Task NotifyEmployeeAsync(LeaveApprovalState state, string template, IReadOnlyDictionary<string, string> placeholders, CancellationToken ct)
    {
        if (state.Employee.UserId == Guid.Empty)
            return;
        await _notifications.SendTemplatedAsync(
            _currentUser.TenantId, state.Employee.UserId, template, placeholders,
            "leave_request", state.Request.Id, ct);
    }

    private async Task NotifyApproverAsync(Guid approverEmployeeId, LeaveApprovalState state, CancellationToken ct)
    {
        var approver = await _employees.GetByIdAsync(_currentUser.TenantId, approverEmployeeId, ct);
        if (approver is null || approver.UserId == Guid.Empty)
            return;
        await _notifications.SendTemplatedAsync(
            _currentUser.TenantId, approver.UserId, "leave_request_next_approval_required",
            new Dictionary<string, string>
            {
                ["employeeName"] = LeaveEntitlementName(state.Employee),
                ["leaveTypeName"] = state.LeaveTypeName,
                ["startDate"] = state.Request.StartAt.ToString("yyyy-MM-dd"),
                ["endDate"] = state.Request.EndAt.ToString("yyyy-MM-dd")
            },
            "leave_request", state.Request.Id, ct);
    }

    private static string LeaveEntitlementName(Employee employee) =>
        $"{employee.FirstName} {employee.LastName}".Trim();
}
