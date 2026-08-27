using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;

namespace ONEVO.Application.Features.Leave.Approval.Queries;

public sealed record ListPendingLeaveApprovalsQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate) : IRequest<Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>>;

public sealed class ListPendingLeaveApprovalsQueryHandler
    : IRequestHandler<ListPendingLeaveApprovalsQuery, Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveApprovalRepository _repository;

    public ListPendingLeaveApprovalsQueryHandler(
        ICurrentUser currentUser, IEmployeeRepository employees, ILeaveApprovalRepository repository)
    {
        _currentUser = currentUser;
        _employees = employees;
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>> Handle(
        ListPendingLeaveApprovalsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Forbidden(LeaveApprovalMessages.TenantMissing);

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.NotFound(LeaveApprovalMessages.NoEmployee);

        var rows = await _repository.ListPendingForApproverAsync(
            _currentUser.TenantId, employee.Id,
            new LeaveApprovalListFilter(query.Search, query.DepartmentId, query.LeaveTypeId, query.FromDate, query.ToDate), ct);

        var actionable = new List<LeavePendingApprovalListItemResponse>();
        foreach (var row in rows)
        {
            var state = await _repository.GetStateAsync(_currentUser.TenantId, row.Request.Id, ct);
            if (state?.ApprovalMode is null)
                continue;
            var modeRows = state.Approvers.Select(x => new ApprovalModeRow(x.ApproverEmployeeId, x.SequenceOrder, x.Status)).ToList();
            if (!LeaveApprovalModeEvaluator.IsActionable(state.ApprovalMode, modeRows, employee.Id))
                continue;
            actionable.Add(LeaveApprovalMapper.ToPendingListItem(row));
        }

        return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Success(actionable);
    }
}

public sealed record ListAllLeaveRequestsQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate) : IRequest<Result<IReadOnlyList<LeaveRequestAllListItemResponse>>>;

public sealed class ListAllLeaveRequestsQueryHandler
    : IRequestHandler<ListAllLeaveRequestsQuery, Result<IReadOnlyList<LeaveRequestAllListItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveApprovalRepository _repository;

    public ListAllLeaveRequestsQueryHandler(ICurrentUser currentUser, ILeaveApprovalRepository repository)
    {
        _currentUser = currentUser;
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<LeaveRequestAllListItemResponse>>> Handle(
        ListAllLeaveRequestsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveRequestAllListItemResponse>>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveRequestAllListItemResponse>>.Forbidden(LeaveApprovalMessages.TenantMissing);

        var rows = await _repository.ListAllAsync(
            _currentUser.TenantId,
            new LeaveRequestAllListFilter(query.Search, query.DepartmentId, query.LeaveTypeId, query.Status, query.FromDate, query.ToDate),
            ct);
        return Result<IReadOnlyList<LeaveRequestAllListItemResponse>>.Success(rows.Select(LeaveApprovalMapper.ToAllListItem).ToList());
    }
}

public sealed record GetLeaveApprovalDetailQuery(Guid RequestId)
    : IRequest<Result<LeaveApprovalDetailResponse>>;

public sealed class GetLeaveApprovalDetailQueryHandler
    : IRequestHandler<GetLeaveApprovalDetailQuery, Result<LeaveApprovalDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveApprovalRepository _repository;
    private readonly ILeaveRequestConflictProvider _conflicts;

    public GetLeaveApprovalDetailQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILeaveApprovalRepository repository,
        ILeaveRequestConflictProvider conflicts)
    {
        _currentUser = currentUser;
        _employees = employees;
        _repository = repository;
        _conflicts = conflicts;
    }

    public async Task<Result<LeaveApprovalDetailResponse>> Handle(GetLeaveApprovalDetailQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveApprovalDetailResponse>.Forbidden(LeaveApprovalMessages.AuthRequired);
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveApprovalDetailResponse>.Forbidden(LeaveApprovalMessages.TenantMissing);

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<LeaveApprovalDetailResponse>.NotFound(LeaveApprovalMessages.NoEmployee);

        var state = await _repository.GetStateAsync(_currentUser.TenantId, query.RequestId, ct);
        if (state is null)
            return Result<LeaveApprovalDetailResponse>.NotFound(LeaveApprovalMessages.NotFound);
        if (state.Approvers.All(x => x.ApproverEmployeeId != employee.Id))
            return Result<LeaveApprovalDetailResponse>.Forbidden(LeaveApprovalMessages.NotAssigned);

        var conflicts = await _conflicts.ListConflictsAsync(
            _currentUser.TenantId, state.Request.EmployeeId, state.Request.StartDate, state.Request.EndDate, ct);
        var warnings = conflicts.Select(c => new LeaveApprovalWarningResponse("current_conflict", c.Title)).ToList();
        var remaining = state.Entitlement is null
            ? 0m
            : LeaveApprovalMapper.CalculateRemaining(
                state.Entitlement.TotalDays, state.Entitlement.CarriedForwardDays,
                state.Entitlement.UsedDays, state.Entitlement.PendingDays);

        return Result<LeaveApprovalDetailResponse>.Success(new LeaveApprovalDetailResponse(
            state.Request.Id,
            state.Request.EmployeeId,
            $"{state.Employee.FirstName} {state.Employee.LastName}".Trim(),
            state.Request.LeaveTypeId,
            state.LeaveTypeName,
            state.LeaveTypeCode,
            state.Request.StartDate,
            state.Request.EndDate,
            state.Request.TotalDays,
            state.Request.PaidDays,
            state.Request.UnpaidDays,
            state.Request.Status,
            state.Request.Reason,
            state.Approvers.Select(a => new LeaveApprovalApproverResponse(
                a.ApproverEmployeeId, a.SequenceOrder, a.Status, a.Comment, a.DelegatedFromApproverId, a.DecidedAt)).ToList(),
            state.InfoMessages.Select(m => new LeaveApprovalInfoMessageResponse(m.SenderEmployeeId, m.Message, m.CreatedAt)).ToList(),
            state.Request.ConflictSnapshotJson,
            warnings,
            remaining));
    }
}
