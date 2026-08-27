using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Cancellation.Mappers;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using ONEVO.Application.Features.Leave.Cancellation.Outbox;
using ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Commands;

public sealed record CancelLeaveRequestCommand(
    Guid RequestId,
    string? Reason,
    DateOnly? EffectiveDate,
    string? ExpectedVersion)
    : IRequest<Result<CancelLeaveRequestResponse>>;

public sealed class CancelLeaveRequestCommandValidator : AbstractValidator<CancelLeaveRequestCommand>
{
    public CancelLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(2000);
        RuleFor(x => x.ExpectedVersion).MaximumLength(32);
    }
}

public sealed class CancelLeaveRequestCommandHandler
    : IRequestHandler<CancelLeaveRequestCommand, Result<CancelLeaveRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveCancellationRepository _repository;
    private readonly LeaveBusinessDateResolver _businessDateResolver;
    private readonly LeaveCancellationClassifier _classifier;
    private readonly LeaveRequestDayAllocationBuilder _allocationBuilder;
    private readonly LeaveRequestDayCalculator _dayCalculator;
    private readonly ILeaveHolidayProvider _holidays;
    private readonly ILeavePolicyRepository _policies;
    private readonly IOutboxWriter _outbox;
    private readonly INotificationDispatcher _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly LeaveCancellationOptions _options;

    public CancelLeaveRequestCommandHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILeaveCancellationRepository repository,
        LeaveBusinessDateResolver businessDateResolver,
        LeaveCancellationClassifier classifier,
        LeaveRequestDayAllocationBuilder allocationBuilder,
        LeaveRequestDayCalculator dayCalculator,
        ILeaveHolidayProvider holidays,
        ILeavePolicyRepository policies,
        IOutboxWriter outbox,
        INotificationDispatcher notifications,
        IDateTimeProvider clock,
        IOptions<LeaveCancellationOptions> options)
    {
        _currentUser = currentUser;
        _employees = employees;
        _repository = repository;
        _businessDateResolver = businessDateResolver;
        _classifier = classifier;
        _allocationBuilder = allocationBuilder;
        _dayCalculator = dayCalculator;
        _holidays = holidays;
        _policies = policies;
        _outbox = outbox;
        _notifications = notifications;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<CancelLeaveRequestResponse>> Handle(CancelLeaveRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CancelLeaveRequestResponse>.Forbidden(LeaveCancellationMessages.AuthRequired);

        var currentEmployee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (currentEmployee is null)
            return Result<CancelLeaveRequestResponse>.Forbidden(LeaveCancellationMessages.NoEmployee);

        var state = await _repository.GetStateAsync(_currentUser.TenantId, command.RequestId, ct);
        if (state is null)
            return Result<CancelLeaveRequestResponse>.NotFound(LeaveCancellationMessages.NotFound);

        var isOwner = state.Request.EmployeeId == currentEmployee.Id;
        var isHrCancel = !isOwner;
        if (isHrCancel && !_currentUser.HasPermission("leave:manage"))
            return Result<CancelLeaveRequestResponse>.Forbidden(LeaveCancellationMessages.NotOwner);

        if (isHrCancel && string.IsNullOrWhiteSpace(command.Reason))
            return Result<CancelLeaveRequestResponse>.Failure(LeaveCancellationMessages.HrReasonRequired);
        if (!isHrCancel && _options.RequireEmployeeReason && string.IsNullOrWhiteSpace(command.Reason))
            return Result<CancelLeaveRequestResponse>.Failure(LeaveCancellationMessages.EmployeeReasonRequired);

        var businessDate = _businessDateResolver.Today(state.LegalEntity?.Timezone);
        var classificationResult = _classifier.Classify(
            state.Request.Status,
            state.Request.StartDate,
            state.Request.EndDate,
            businessDate,
            command.EffectiveDate);
        if (!classificationResult.IsSuccess)
            return Result<CancelLeaveRequestResponse>.Failure(
                classificationResult.Error!, classificationResult.StatusCode ?? 400);

        var classification = classificationResult.Value!;
        var now = _clock.UtcNow;
        var trimmedReason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();

        _repository.SetExpectedVersion(state.Request, command.ExpectedVersion);

        var openApproverIds = state.Approvers
            .Where(a => a.Status is LeaveRequestApproverStatuses.Pending or LeaveRequestApproverStatuses.InformationRequested)
            .Select(a => a.ApproverEmployeeId)
            .ToHashSet();

        var allocations = (await _repository.ListAllocationsAsync(_currentUser.TenantId, state.Request.Id, ct)).ToList();
        if (allocations.Count == 0)
        {
            var backfilled = await BuildLegacyAllocationsAsync(state, now, ct);
            if (classification.Kind == LeaveCancellationKind.ApprovedPartial
                && backfilled.Sum(x => x.DayUnit) != state.Request.TotalDays)
            {
                return Result<CancelLeaveRequestResponse>.Conflict(LeaveCancellationMessages.AllocationUnavailable);
            }

            if (backfilled.Count > 0)
            {
                await _repository.AddAllocationsAsync(backfilled, ct);
                allocations = backfilled.ToList();
            }
        }

        decimal releasedPendingDays = 0m;
        decimal restoredUsedDays = 0m;
        decimal affectedUnpaidDays = 0m;
        var isPartial = classification.Kind == LeaveCancellationKind.ApprovedPartial;

        if (classification.Kind == LeaveCancellationKind.PendingStyle)
        {
            releasedPendingDays = state.Request.PaidDays;
            if (state.Entitlement is not null)
            {
                state.Entitlement.PendingDays = Math.Max(0m, state.Entitlement.PendingDays - releasedPendingDays);
                state.Entitlement.UpdatedAt = now;
            }

            foreach (var approver in state.Approvers.Where(a =>
                a.Status is LeaveRequestApproverStatuses.Pending or LeaveRequestApproverStatuses.InformationRequested))
            {
                approver.Status = LeaveRequestApproverStatuses.Cancelled;
                approver.DecidedAt = now;
            }

            CancelAllocations(allocations.Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active), now);
            MarkRequestCancelled(state.Request, trimmedReason, null, now);
        }
        else if (classification.Kind == LeaveCancellationKind.ApprovedFull)
        {
            restoredUsedDays = state.Request.PaidDays;
            affectedUnpaidDays = state.Request.UnpaidDays;
            if (state.Entitlement is not null)
            {
                state.Entitlement.UsedDays = Math.Max(0m, state.Entitlement.UsedDays - restoredUsedDays);
                state.Entitlement.UpdatedAt = now;
            }

            CancelAllocations(allocations.Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active), now);
            MarkRequestCancelled(state.Request, trimmedReason, null, now);
            await AddAdjustmentAuditAsync(state, restoredUsedDays, isHrCancel, trimmedReason, false, now, ct);
        }
        else
        {
            var effectiveDate = classification.EffectiveDate!.Value;
            var futureAllocations = allocations
                .Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active && a.LeaveDate >= effectiveDate)
                .ToList();
            restoredUsedDays = futureAllocations.Sum(a => a.PaidUnit);
            affectedUnpaidDays = futureAllocations.Sum(a => a.UnpaidUnit);
            if (restoredUsedDays <= 0m && affectedUnpaidDays <= 0m)
                return Result<CancelLeaveRequestResponse>.Conflict(LeaveCancellationMessages.NoRestorableDays);

            if (state.Entitlement is not null && restoredUsedDays > 0m)
            {
                state.Entitlement.UsedDays = Math.Max(0m, state.Entitlement.UsedDays - restoredUsedDays);
                state.Entitlement.UpdatedAt = now;
            }

            CancelAllocations(futureAllocations, now);
            MarkRequestCancelled(state.Request, trimmedReason, effectiveDate, now);
            if (restoredUsedDays > 0m)
                await AddAdjustmentAuditAsync(state, restoredUsedDays, isHrCancel, trimmedReason, true, now, ct);
        }

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.LeaveRequestCancelled,
            new LeaveRequestCancelledPayload(
                _currentUser.TenantId,
                state.Request.Id,
                state.Request.EmployeeId,
                state.Request.LeaveTypeId,
                state.LeaveTypeName,
                state.Request.StartDate,
                state.Request.EndDate,
                isPartial,
                state.Request.PartialCancelEffectiveDate,
                releasedPendingDays,
                restoredUsedDays,
                affectedUnpaidDays,
                _currentUser.UserId,
                currentEmployee.Id,
                isHrCancel,
                trimmedReason,
                now),
            _currentUser.TenantId,
            ct);

        await NotifyAsync(state, currentEmployee.Id, isHrCancel, isPartial, openApproverIds, restoredUsedDays, trimmedReason, ct);

        try
        {
            await _repository.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<CancelLeaveRequestResponse>.Conflict(LeaveCancellationMessages.Concurrency);
        }

        var remaining = await RemainingAsync(state, businessDate, ct);
        return Result<CancelLeaveRequestResponse>.Success(LeaveCancellationMapper.ToResponse(
            state.Request, isPartial, releasedPendingDays, restoredUsedDays, remaining, now));
    }

    private async Task<IReadOnlyList<LeaveRequestDayAllocation>> BuildLegacyAllocationsAsync(
        LeaveCancellationState state, DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyCollection<int> workingDays = [];
        if (state.Employee.LegalEntityId is Guid legalEntityId)
        {
            var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
                _currentUser.TenantId, [legalEntityId], state.Request.StartDate.Year, ct);
            if (policies.TryGetValue(legalEntityId, out var policy))
            {
                var assignment = policy.LegalEntities.FirstOrDefault(x => x.Assignment.LegalEntityId == legalEntityId);
                try
                {
                    workingDays = LegalEntityMapper.ParseStandardWorkingDays(assignment?.StandardWorkingDaysJson ?? "[]");
                }
                catch (Exception)
                {
                    workingDays = [];
                }
            }
        }

        var holidays = await _holidays.ListHolidaysAsync(
            _currentUser.TenantId, state.Employee.LegalEntityId, state.Request.StartDate, state.Request.EndDate, ct);
        var calculated = _dayCalculator.Calculate(new LeaveRequestDayCalculationInput(
            state.Request.StartDate, state.Request.EndDate, state.Request.HalfDayPeriod, workingDays, holidays));
        if (calculated.CountedDates.Count == 0)
            return [];

        try
        {
            var drafts = _allocationBuilder.Build(
                calculated.CountedDates, state.Request.HalfDayPeriod, state.Request.PaidDays, state.Request.UnpaidDays);
            return _allocationBuilder.ToEntities(_currentUser.TenantId, state.Request.Id, drafts, now);
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private async Task AddAdjustmentAuditAsync(
        LeaveCancellationState state,
        decimal restoredUsedDays,
        bool isHrCancel,
        string? reason,
        bool isPartial,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (state.Entitlement is null || restoredUsedDays <= 0m)
            return;

        var remaining = LeaveEntitlementMapper.Remaining(state.Entitlement);
        await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            EmployeeId = state.Request.EmployeeId,
            LeaveTypeId = state.Request.LeaveTypeId,
            ChangeType = LeaveBalanceChangeTypes.Adjustment,
            DaysChanged = restoredUsedDays,
            BalanceAfter = remaining,
            Reason = isPartial
                ? (isHrCancel ? "Leave request partially cancelled by HR" : "Leave request partially cancelled")
                : (isHrCancel ? "Leave request cancelled by HR" : "Leave request cancelled"),
            RelatedRequestId = state.Request.Id,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        }, ct);
    }

    private async Task NotifyAsync(
        LeaveCancellationState state,
        Guid cancelledByEmployeeId,
        bool isHrCancel,
        bool isPartial,
        HashSet<Guid> openApproverIds,
        decimal restoredDays,
        string? reason,
        CancellationToken ct)
    {
        var employeeName = LeaveEntitlementMapper.EmployeeName(state.Employee.FirstName, state.Employee.LastName);
        var cancelledByName = isHrCancel
            ? "HR"
            : employeeName;
        var placeholders = new Dictionary<string, string>
        {
            ["employeeName"] = employeeName,
            ["cancelledByName"] = cancelledByName,
            ["leaveTypeName"] = state.LeaveTypeName,
            ["startDate"] = state.Request.StartDate.ToString("yyyy-MM-dd"),
            ["endDate"] = state.Request.EndDate.ToString("yyyy-MM-dd"),
            ["effectiveDate"] = state.Request.PartialCancelEffectiveDate?.ToString("yyyy-MM-dd") ?? "",
            ["restoredDays"] = restoredDays.ToString("0.#"),
            ["reason"] = reason ?? ""
        };

        var template = isPartial
            ? "leave_request_partially_cancelled"
            : isHrCancel
                ? "leave_request_cancelled_by_hr"
                : "leave_request_cancelled_by_employee";

        if (isHrCancel)
        {
            if (state.Employee.UserId != Guid.Empty)
            {
                await _notifications.SendTemplatedAsync(
                    _currentUser.TenantId, state.Employee.UserId, template, placeholders,
                    "leave_request", state.Request.Id, ct);
            }

            return;
        }

        var notifyIds = isPartial || openApproverIds.Count == 0
            ? state.ApproverRecipients.Select(x => x.EmployeeId).ToHashSet()
            : openApproverIds;
        foreach (var recipient in state.ApproverRecipients
                     .Where(x => notifyIds.Contains(x.EmployeeId) && x.UserId is { } userId && userId != Guid.Empty)
                     .GroupBy(x => x.UserId)
                     .Select(g => g.First()))
        {
            await _notifications.SendTemplatedAsync(
                _currentUser.TenantId, recipient.UserId!.Value, template, placeholders,
                "leave_request", state.Request.Id, ct);
        }
    }

    private async Task<decimal> RemainingAsync(LeaveCancellationState state, DateOnly businessDate, CancellationToken ct)
    {
        if (state.Entitlement is null)
            return 0m;

        LeavePolicyAggregate? policy = null;
        if (state.Employee.LegalEntityId is Guid legalEntityId)
        {
            var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
                _currentUser.TenantId, [legalEntityId], state.Request.StartDate.Year, ct);
            policies.TryGetValue(legalEntityId, out policy);
        }

        var expiry = LeaveEntitlementPlanner.CarryExpiryFromPolicy(policy, state.Request.LeaveTypeId, state.Request.StartDate.Year);
        var carry = LeaveEntitlementMapper.EffectiveCarry(state.Entitlement.CarriedForwardDays, expiry, businessDate);
        return LeaveEntitlementMapper.Remaining(
            state.Entitlement.TotalDays, carry, state.Entitlement.UsedDays, state.Entitlement.PendingDays);
    }

    private static void MarkRequestCancelled(LeaveRequest request, string? reason, DateOnly? effectiveDate, DateTimeOffset now)
    {
        request.Status = LeaveRequestStatuses.Cancelled;
        request.CancellationReason = reason;
        request.PartialCancelEffectiveDate = effectiveDate;
        request.UpdatedAt = now;
    }

    private static void CancelAllocations(IEnumerable<LeaveRequestDayAllocation> allocations, DateTimeOffset now)
    {
        foreach (var allocation in allocations)
        {
            allocation.Status = LeaveRequestDayAllocationStatuses.Cancelled;
            allocation.CancelledAt = now;
        }
    }
}
