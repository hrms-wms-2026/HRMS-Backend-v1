using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.Mappers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Request.Queries.ListMyLeaveRequests;

public sealed class ListMyLeaveRequestsQueryHandler
    : IRequestHandler<ListMyLeaveRequestsQuery, Result<IReadOnlyList<LeaveRequestListItemResponse>>>
{
    private static readonly string[] AllowedStatuses =
        [LeaveRequestStatuses.Pending, LeaveRequestStatuses.Approved, LeaveRequestStatuses.Rejected, LeaveRequestStatuses.Cancelled];

    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveRequestRepository _requests;

    public ListMyLeaveRequestsQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILeaveRequestRepository requests)
    {
        _currentUser = currentUser;
        _employees = employees;
        _requests = requests;
    }

    public async Task<Result<IReadOnlyList<LeaveRequestListItemResponse>>> Handle(
        ListMyLeaveRequestsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveRequestListItemResponse>>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveRequestListItemResponse>>.Forbidden("Tenant context missing.");

        if (!string.IsNullOrWhiteSpace(query.Status) && !AllowedStatuses.Contains(query.Status))
            return Result<IReadOnlyList<LeaveRequestListItemResponse>>.Failure("Status filter is not valid.");

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<LeaveRequestListItemResponse>>.NotFound(LeaveRequestMessages.NoEmployeeRecord);

        var rows = await _requests.ListOwnAsync(
            _currentUser.TenantId,
            employee.Id,
            new LeaveRequestListFilter(query.Status, query.FromDate, query.ToDate, query.LeaveTypeId),
            ct);

        return Result<IReadOnlyList<LeaveRequestListItemResponse>>.Success(
            rows.Select(row => LeaveRequestMapper.ToListItem(row.Request, row.LeaveTypeName, row.LeaveTypeCode)).ToList());
    }
}
