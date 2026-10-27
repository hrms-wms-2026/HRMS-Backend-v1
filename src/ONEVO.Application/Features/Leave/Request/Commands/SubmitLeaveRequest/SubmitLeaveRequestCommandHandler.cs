using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Mappers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;

public sealed class SubmitLeaveRequestCommandHandler
    : IRequestHandler<SubmitLeaveRequestCommand, Result<LeaveRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly LeaveRequestSubmissionEvaluator _evaluator;
    private readonly ILeaveRequestRepository _requests;
    private readonly LeaveRequestDayAllocationBuilder _allocationBuilder;
    private readonly LeaveApprovalDecisionService _decisions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxChangeNotifier? _inbox;

    public SubmitLeaveRequestCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        LeaveRequestSubmissionEvaluator evaluator,
        ILeaveRequestRepository requests,
        LeaveRequestDayAllocationBuilder allocationBuilder,
        LeaveApprovalDecisionService decisions,
        IUnitOfWork unitOfWork,
        IInboxChangeNotifier? inbox = null)
    {
        _currentUser = currentUser;
        _clock = clock;
        _evaluator = evaluator;
        _requests = requests;
        _allocationBuilder = allocationBuilder;
        _decisions = decisions;
        _unitOfWork = unitOfWork;
        _inbox = inbox;
    }

    public async Task<Result<LeaveRequestResponse>> Handle(SubmitLeaveRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveRequestResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveRequestResponse>.Forbidden("Tenant context missing.");

        var evaluation = await _evaluator.EvaluateAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            command.IsOnBehalfRequest ? command.EmployeeId : null,
            command.LeaveTypeId,
            command.StartAt,
            command.EndAt,
            command.Reason,
            command.FileRecordIds,
            ct);
        if (!evaluation.IsSuccess)
            return Result<LeaveRequestResponse>.Failure(evaluation.Error!, evaluation.StatusCode ?? 400);

        var draft = evaluation.Value!;
        var now = _clock.UtcNow;
        var requestId = Guid.NewGuid();
        var snapshotJson = LeaveRequestMapper.ToConflictSnapshotJson(
            draft.Warnings, draft.CalendarConflicts, draft.TeamAbsencePercent);

        var request = new LeaveRequest
        {
            Id = requestId,
            TenantId = _currentUser.TenantId,
            EmployeeId = draft.TargetEmployee.Id,
            LeaveTypeId = command.LeaveTypeId,
            StartAt = command.StartAt,
            EndAt = command.EndAt,
            TotalHours = draft.TotalHours,
            PaidHours = draft.PaidHours,
            UnpaidHours = draft.UnpaidHours,
            LeavePolicyVersionId = draft.PolicyVersionId,
            ExceedsBalanceByHours = draft.ExceedsBalanceByHours,
            Reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
            Status = LeaveRequestStatuses.Pending,
            ConflictSnapshotJson = snapshotJson,
            NoticePeriodMissed = draft.NoticePeriodMissed,
            SubmittedOnBehalfOfBy = command.IsOnBehalfRequest ? _currentUser.UserId : null,
            CreatedAt = now
        };

        var approvers = (draft.ApprovalMode == LeaveApprovalModes.None
            ? []
            : draft.Approvers.Approvers).Select(row => new LeaveRequestApprover
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LeaveRequestId = requestId,
            ApproverEmployeeId = row.ApproverEmployeeId,
            SequenceOrder = row.SequenceOrder,
            Status = LeaveRequestApproverStatuses.Pending,
            DelegatedFromApproverId = row.DelegatedFromApproverId
        }).ToList();

        var documents = command.FileRecordIds.Distinct().Select(fileId => new LeaveRequestDocument
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LeaveRequestId = requestId,
            FileRecordId = fileId
        }).ToList();

        var allocationDrafts = _allocationBuilder.Build(
            draft.CountedDates,
            draft.HoursByDate,
            draft.PaidHours,
            draft.UnpaidHours);
        var dayAllocations = _allocationBuilder.ToEntities(
            _currentUser.TenantId, requestId, allocationDrafts, now);

        var pendingBeforeSubmit = draft.Entitlement.PendingHours;

        // The approvers' (and the employee's) open inboxes refresh once the request commits. Enqueued before the
        // repository's SaveChanges so the outbox row lands in the same transaction.
        if (_inbox is not null)
            await _inbox.EnqueueAsync(_currentUser.TenantId,
                approvers.Select(a => a.ApproverEmployeeId).Append(request.EmployeeId), InboxRefs.LeaveRequest(requestId), ct);

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                await _requests.AddPendingRequestAsync(
                    new LeaveRequestWriteSet(request, approvers, documents, dayAllocations, draft.Entitlement),
                    transactionCt);

                if (draft.ApprovalMode == LeaveApprovalModes.None)
                {
                    var finalized = await _decisions.FinalizeNoApprovalAsync(requestId, transactionCt);
                    if (!finalized.IsSuccess)
                        throw new AutomaticApprovalException(
                            finalized.Error!, finalized.StatusCode ?? 500, finalized.ErrorCode);
                }

                return true;
            }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == LeaveRequestMessages.Overlap)
        {
            return Result<LeaveRequestResponse>.Conflict(LeaveRequestMessages.Overlap);
        }
        catch (AutomaticApprovalException ex)
        {
            return Result<LeaveRequestResponse>.Failure(ex.Message, ex.StatusCode, ex.ErrorCode);
        }

        var snapshot = new LeaveRequestConflictSnapshotResponse(
            draft.Warnings,
            draft.CalendarConflicts.Select(c => new LeaveRequestCalendarConflictResponse(
                c.Source, c.Title, c.StartsAt, c.EndsAt)).ToList(),
            draft.TeamAbsencePercent);

        return Result<LeaveRequestResponse>.Success(LeaveRequestMapper.ToResponse(
            request,
            draft.LeaveType.Name,
            draft.LeaveType.Code,
            approvers,
            LeaveRequestMapper.ToBalanceImpact(draft.CurrentRemaining, pendingBeforeSubmit, draft.PaidHours),
            snapshot));
    }

    private sealed class AutomaticApprovalException(
        string message,
        int statusCode,
        string? errorCode) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
        public string? ErrorCode { get; } = errorCode;
    }
}
