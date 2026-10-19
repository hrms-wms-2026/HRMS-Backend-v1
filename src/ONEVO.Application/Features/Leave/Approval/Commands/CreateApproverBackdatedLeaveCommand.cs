using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Mappers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

/// <summary>
/// A leave approver turns one past working day of an employee into leave (plan E2, an attendance alert converted to
/// leave). Every submission rule runs (balance, policy, leave type, gender, overlap, documents...) except
/// AllowBackdatedRequests; the request is created already approved by the caller: approver rows decided, hours
/// deducted from the balance with an audit row, and the usual "leave approved" outbox message and notification.
/// Approver-only: the caller needs leave:approve and, when the leave type needs approval, must be one of the
/// employee's resolved leave approvers. Returns the new request id.
/// </summary>
public sealed record CreateApproverBackdatedLeaveCommand(Guid EmployeeId, Guid LeaveTypeId, DateOnly WorkDate, string? Reason = null)
    : IRequest<Result<Guid>>;

public sealed class CreateApproverBackdatedLeaveCommandHandler : IRequestHandler<CreateApproverBackdatedLeaveCommand, Result<Guid>>
{
    public const string ApprovePermission = "leave:approve";
    public const string NotAnApprover = "Only a leave approver of this employee can turn a day into leave.";
    public const string NotPast = "Only a past day can be turned into leave.";

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employees;
    private readonly ILeavePolicyRepository _policies;
    private readonly LeaveRequestSubmissionEvaluator _evaluator;
    private readonly ILeaveRequestRepository _requests;
    private readonly LeaveRequestDayAllocationBuilder _allocationBuilder;
    private readonly ILeaveApprovalRepository _approvals;
    private readonly IOutboxWriter _outbox;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public CreateApproverBackdatedLeaveCommandHandler(
        ICurrentUser currentUser, IDateTimeProvider clock, IEmployeeRepository employees, ILeavePolicyRepository policies,
        LeaveRequestSubmissionEvaluator evaluator, ILeaveRequestRepository requests, LeaveRequestDayAllocationBuilder allocationBuilder,
        ILeaveApprovalRepository approvals, IOutboxWriter outbox, INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _clock = clock;
        _employees = employees;
        _policies = policies;
        _evaluator = evaluator;
        _requests = requests;
        _allocationBuilder = allocationBuilder;
        _approvals = approvals;
        _outbox = outbox;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateApproverBackdatedLeaveCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (!_currentUser.HasPermission(ApprovePermission))
            return Result<Guid>.Forbidden(NotAnApprover);
        var tenantId = _currentUser.TenantId;
        var caller = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<Guid>.NotFound(LeaveApprovalMessages.NoEmployee);
        if (caller.Id == command.EmployeeId)
            return Result<Guid>.Conflict(LeaveApprovalMessages.SelfApproval);
        if (command.WorkDate >= _clock.Today)
            return Result<Guid>.UnprocessableEntity(NotPast);

        var employee = await _employees.GetByIdAsync(tenantId, command.EmployeeId, ct);
        if (employee is null)
            return Result<Guid>.NotFound(LeaveRequestMessages.EmployeeNotFound);
        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<Guid>.UnprocessableEntity(LeaveRequestMessages.NoEntitlement);
        var legalEntity = (await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, [legalEntityId], ct)).FirstOrDefault();
        if (legalEntity?.WorkStartTime is not TimeOnly workStart || legalEntity.WorkEndTime is not TimeOnly workEnd)
            return Result<Guid>.UnprocessableEntity(LeaveRequestMessages.WorkWindowRequired);

        // The company's working day on that date, in its own time zone.
        var zone = LeaveCancellationOptions.ResolveTimezone(legalEntity.Timezone) ?? TimeZoneInfo.Utc;
        DateTimeOffset Local(TimeOnly time)
        {
            var local = command.WorkDate.ToDateTime(time);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        var startAt = Local(workStart);
        var endAt = Local(workEnd);
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Converted from an attendance alert" : command.Reason.Trim();

        var evaluation = await _evaluator.EvaluateAsync(
            tenantId, _currentUser.UserId, command.EmployeeId, command.LeaveTypeId, startAt, endAt, reason, [], ct,
            new LeaveEvaluationOverrides(AllowBackdated: true));
        if (!evaluation.IsSuccess)
            return Result<Guid>.Failure(evaluation.Error!, evaluation.StatusCode is null or 400 ? 422 : evaluation.StatusCode.Value);
        var draft = evaluation.Value!;
        var chain = draft.Approvers.Approvers;
        if (chain.Count > 0 && chain.All(a => a.ApproverEmployeeId != caller.Id))
            return Result<Guid>.Forbidden(NotAnApprover);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async t => await CreateApprovedAsync(command, caller.Id, draft, startAt, endAt, reason, t), ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == LeaveRequestMessages.Overlap)
        {
            _unitOfWork.ClearTracking();
            return Result<Guid>.Conflict(LeaveRequestMessages.Overlap);
        }
    }

    private async Task<Result<Guid>> CreateApprovedAsync(
        CreateApproverBackdatedLeaveCommand command, Guid callerEmployeeId, LeaveRequestEvaluation draft,
        DateTimeOffset startAt, DateTimeOffset endAt, string reason, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;
        var requestId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = requestId,
            TenantId = tenantId,
            EmployeeId = command.EmployeeId,
            LeaveTypeId = command.LeaveTypeId,
            StartAt = startAt,
            EndAt = endAt,
            TotalHours = draft.TotalHours,
            PaidHours = draft.PaidHours,
            UnpaidHours = draft.UnpaidHours,
            Reason = reason,
            Status = LeaveRequestStatuses.Approved,
            ApprovedBy = callerEmployeeId,
            ApprovedAt = now,
            ConflictSnapshotJson = LeaveRequestMapper.ToConflictSnapshotJson(draft.Warnings, draft.CalendarConflicts, draft.TeamAbsencePercent),
            NoticePeriodMissed = draft.NoticePeriodMissed,
            SubmittedOnBehalfOfBy = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // The caller decides their own row; the rest of the chain is skipped, as when one approval completes a request.
        var approvers = draft.Approvers.Approvers.Select(row => new LeaveRequestApprover
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeaveRequestId = requestId,
            ApproverEmployeeId = row.ApproverEmployeeId,
            SequenceOrder = row.SequenceOrder,
            Status = row.ApproverEmployeeId == callerEmployeeId ? LeaveRequestApproverStatuses.Approved : LeaveRequestApproverStatuses.Skipped,
            Comment = row.ApproverEmployeeId == callerEmployeeId ? reason : null,
            DecidedAt = now,
            DelegatedFromApproverId = row.DelegatedFromApproverId,
        }).ToList();
        var allocations = _allocationBuilder.ToEntities(tenantId, requestId,
            _allocationBuilder.Build(draft.CountedDates, draft.HoursByDate, draft.PaidHours, draft.UnpaidHours), now);

        // Written as a pending request first (overlap check + reserved hours), then the reserve moves to used at once.
        await _requests.AddPendingRequestAsync(new LeaveRequestWriteSet(request, approvers, [], allocations, draft.Entitlement), ct);
        var entitlement = draft.Entitlement;
        entitlement.PendingHours -= request.PaidHours;
        entitlement.UsedHours += request.PaidHours;
        entitlement.UpdatedAt = now;
        if (request.PaidHours > 0m)
        {
            await _approvals.AddBalanceAuditAsync(new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = request.EmployeeId,
                LeaveTypeId = request.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Deduction,
                HoursChanged = -request.PaidHours,
                BalanceAfter = LeaveApprovalMapper.CalculateRemaining(
                    entitlement.TotalHours, entitlement.CarriedForwardHours, entitlement.UsedHours, entitlement.PendingHours),
                Reason = "Leave approved by the approver (converted from an attendance alert)",
                RelatedRequestId = requestId,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId,
            }, ct);
        }
        await _outbox.EnqueueAsync(OutboxMessageTypes.LeaveRequestApproved, new LeaveRequestApprovedPayload(
            tenantId, requestId, request.EmployeeId, request.LeaveTypeId, request.StartAt, request.EndAt,
            request.PaidHours, request.UnpaidHours, callerEmployeeId), tenantId, ct);
        if (draft.TargetEmployee.UserId != Guid.Empty)
        {
            await _notifications.SendTemplatedAsync(tenantId, draft.TargetEmployee.UserId, "leave_request_approved",
                new Dictionary<string, string>
                {
                    ["leaveTypeName"] = draft.LeaveType.Name,
                    ["startDate"] = request.StartAt.ToString("yyyy-MM-dd"),
                    ["endDate"] = request.EndAt.ToString("yyyy-MM-dd"),
                },
                ONEVO.Application.Features.Inbox.Contracts.InboxNotificationNavigation.RelatedEntityType("leave", "request"), requestId, ct);
        }
        await _approvals.SaveChangesAsync(ct);
        return Result<Guid>.Success(requestId);
    }
}
