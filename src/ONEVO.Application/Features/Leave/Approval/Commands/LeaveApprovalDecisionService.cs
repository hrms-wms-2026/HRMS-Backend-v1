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
    private readonly LeaveApprovalOptions _options;

    public LeaveApprovalDecisionService(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IEmployeeRepository employees,
        ILeaveApprovalRepository repository,
        IOutboxWriter outbox,
        INotificationDispatcher notifications,
        ILeaveRequestConflictProvider conflicts,
        IOptions<LeaveApprovalOptions> options)
    {
        _currentUser = currentUser;
        _clock = clock;
        _employees = employees;
        _repository = repository;
        _outbox = outbox;
        _notifications = notifications;
        _conflicts = conflicts;
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
            state.Entitlement.PendingDays -= state.Request.PaidDays;
            state.Entitlement.UpdatedAt = _clock.UtcNow;
        }

        await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestRejected, new LeaveRequestRejectedPayload(
            _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
            state.Request.StartDate, state.Request.EndDate, state.Request.PaidDays, state.Request.UnpaidDays,
            currentEmployee.Id, reason.Trim()), _currentUser.TenantId, ct);

        await NotifyEmployeeAsync(state, "leave_request_rejected", new Dictionary<string, string>
        {
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartDate.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndDate.ToString("yyyy-MM-dd"),
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
            state.Request.StartDate, state.Request.EndDate, currentEmployee.Id, question.Trim()), _currentUser.TenantId, ct);

        var approverName = LeaveEntitlementName(currentEmployee);
        await NotifyEmployeeAsync(state, "leave_request_information_requested", new Dictionary<string, string>
        {
            ["approverName"] = approverName,
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartDate.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndDate.ToString("yyyy-MM-dd")
        }, ct);

        await _repository.SaveChangesAsync(ct);
        return Result<LeaveApprovalDecisionResponse>.Success(await MapAsync(state, 0m, ct));
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
            state.Entitlement.TotalDays, state.Entitlement.CarriedForwardDays,
            state.Entitlement.UsedDays, state.Entitlement.PendingDays);
        if (state.Request.PaidDays > 0m &&
            (state.Entitlement.PendingDays < state.Request.PaidDays || remainingBefore < 0m))
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
            paidMoved = state.Request.PaidDays;
            state.Entitlement.PendingDays -= state.Request.PaidDays;
            state.Entitlement.UsedDays += state.Request.PaidDays;
            state.Entitlement.UpdatedAt = _clock.UtcNow;

            var balanceAfter = LeaveApprovalMapper.CalculateRemaining(
                state.Entitlement.TotalDays, state.Entitlement.CarriedForwardDays,
                state.Entitlement.UsedDays, state.Entitlement.PendingDays);
            if (state.Request.PaidDays > 0m)
            {
                await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
                {
                    Id = Guid.NewGuid(),
                    TenantId = _currentUser.TenantId,
                    EmployeeId = state.Request.EmployeeId,
                    LeaveTypeId = state.Request.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Deduction,
                    DaysChanged = -state.Request.PaidDays,
                    BalanceAfter = balanceAfter,
                    Reason = "Leave request approved",
                    RelatedRequestId = state.Request.Id,
                    CreatedAt = _clock.UtcNow,
                    CreatedBy = _currentUser.UserId
                }, ct);
            }

            await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestApproved, new LeaveRequestApprovedPayload(
                _currentUser.TenantId, state.Request.Id, state.Request.EmployeeId, state.Request.LeaveTypeId,
                state.Request.StartDate, state.Request.EndDate, state.Request.PaidDays, state.Request.UnpaidDays,
                currentEmployee.Id), _currentUser.TenantId, ct);

            await NotifyEmployeeAsync(state, "leave_request_approved", new Dictionary<string, string>
            {
                ["leaveTypeName"] = state.LeaveTypeName,
                ["startDate"] = state.Request.StartDate.ToString("yyyy-MM-dd"),
                ["endDate"] = state.Request.EndDate.ToString("yyyy-MM-dd")
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
                state.Entitlement.TotalDays, state.Entitlement.CarriedForwardDays,
                state.Entitlement.UsedDays, state.Entitlement.PendingDays);
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
            _currentUser.TenantId, state.Request.EmployeeId, state.Request.StartDate, state.Request.EndDate, ct);
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
                ["startDate"] = state.Request.StartDate.ToString("yyyy-MM-dd"),
                ["endDate"] = state.Request.EndDate.ToString("yyyy-MM-dd")
            },
            "leave_request", state.Request.Id, ct);
    }

    private static string LeaveEntitlementName(Employee employee) =>
        $"{employee.FirstName} {employee.LastName}".Trim();
}
