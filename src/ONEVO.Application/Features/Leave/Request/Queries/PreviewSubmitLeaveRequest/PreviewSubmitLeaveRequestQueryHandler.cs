using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Mappers;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Request.Queries.PreviewSubmitLeaveRequest;

public sealed class PreviewSubmitLeaveRequestQueryHandler
    : IRequestHandler<PreviewSubmitLeaveRequestQuery, Result<LeaveRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly LeaveRequestSubmissionEvaluator _evaluator;

    public PreviewSubmitLeaveRequestQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        LeaveRequestSubmissionEvaluator evaluator)
    {
        _currentUser = currentUser;
        _clock = clock;
        _evaluator = evaluator;
    }

    public async Task<Result<LeaveRequestResponse>> Handle(PreviewSubmitLeaveRequestQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveRequestResponse>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveRequestResponse>.Forbidden("Tenant context missing.");

        var evaluation = await _evaluator.EvaluateAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            query.IsOnBehalfRequest ? query.EmployeeId : null,
            query.LeaveTypeId,
            query.StartDate,
            query.EndDate,
            query.HalfDayPeriod,
            query.Reason,
            query.FileRecordIds,
            ct);
        if (!evaluation.IsSuccess)
            return Result<LeaveRequestResponse>.Failure(evaluation.Error!, evaluation.StatusCode ?? 400);

        var draft = evaluation.Value!;
        var snapshot = new LeaveRequestConflictSnapshotResponse(
            draft.Warnings,
            draft.CalendarConflicts.Select(c => new LeaveRequestCalendarConflictResponse(
                c.Source, c.Title, c.StartsAt, c.EndsAt)).ToList(),
            draft.TeamAbsencePercent);

        return Result<LeaveRequestResponse>.Success(new LeaveRequestResponse(
            Guid.Empty,
            draft.TargetEmployee.Id,
            query.LeaveTypeId,
            draft.LeaveType.Name,
            draft.LeaveType.Code,
            query.StartDate,
            query.EndDate,
            query.HalfDayPeriod,
            draft.TotalDays,
            draft.PaidDays,
            draft.UnpaidDays,
            LeaveRequestStatuses.Pending,
            draft.NoticePeriodMissed,
            query.IsOnBehalfRequest ? _currentUser.UserId : null,
            LeaveRequestMapper.ToBalanceImpact(draft.CurrentRemaining, draft.Entitlement.PendingDays, draft.PaidDays),
            draft.Approvers.Approvers.Select(a => new LeaveRequestApproverResponse(
                a.ApproverEmployeeId, a.SequenceOrder, LeaveRequestApproverStatuses.Pending, a.DelegatedFromApproverId)).ToList(),
            snapshot,
            _clock.UtcNow));
    }
}
