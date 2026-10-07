using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
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
    private readonly IEmployeeRepository _employees;

    public PreviewSubmitLeaveRequestQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        LeaveRequestSubmissionEvaluator evaluator,
        IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _clock = clock;
        _evaluator = evaluator;
        _employees = employees;
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
            query.StartAt,
            query.EndAt,
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

        // Name the approver so the wizard's review step can say who the request goes to.
        var approverIds = draft.Approvers.Approvers.Select(a => a.ApproverEmployeeId).Distinct().ToList();
        var approverPeople = approverIds.Count == 0
            ? (IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>)new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : await _employees.ListByIdsAsync(_currentUser.TenantId, approverIds, ct);

        return Result<LeaveRequestResponse>.Success(new LeaveRequestResponse(
            Guid.Empty,
            draft.TargetEmployee.Id,
            query.LeaveTypeId,
            draft.LeaveType.Name,
            draft.LeaveType.Code,
            query.StartAt,
            query.EndAt,
            draft.TotalHours,
            draft.PaidHours,
            draft.UnpaidHours,
            LeaveRequestStatuses.Pending,
            draft.NoticePeriodMissed,
            query.IsOnBehalfRequest ? _currentUser.UserId : null,
            LeaveRequestMapper.ToBalanceImpact(draft.CurrentRemaining, draft.Entitlement.PendingHours, draft.PaidHours),
            draft.Approvers.Approvers.Select(a => new LeaveRequestApproverResponse(
                a.ApproverEmployeeId,
                approverPeople.TryGetValue(a.ApproverEmployeeId, out var person) ? $"{person.FirstName} {person.LastName}".Trim() : string.Empty,
                a.SequenceOrder,
                LeaveRequestApproverStatuses.Pending,
                a.DelegatedFromApproverId)).ToList(),
            snapshot,
            _clock.UtcNow));
    }
}
