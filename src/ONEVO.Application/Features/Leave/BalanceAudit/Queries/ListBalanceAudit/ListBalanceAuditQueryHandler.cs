using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.Mappers;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

public class ListBalanceAuditQueryHandler : IRequestHandler<ListBalanceAuditQuery, Result<IReadOnlyList<LeaveBalanceAuditResponse>>>
{
    private readonly ILeaveBalanceAuditRepository _audits;
    private readonly ICurrentUser _currentUser;

    public ListBalanceAuditQueryHandler(ILeaveBalanceAuditRepository audits, ICurrentUser currentUser)
    {
        _audits = audits;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceAuditResponse>>> Handle(ListBalanceAuditQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveBalanceAuditResponse>>.Forbidden("Authentication required.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 25 : Math.Min(request.PageSize, 5000);

        var rows = await _audits.ListRowsAsync(
            _currentUser.TenantId,
            new LeaveBalanceAuditListFilter(
                request.EmployeeId, request.LeaveTypeId, request.ChangeType, request.FromDate, request.ToDate, page, pageSize),
            ct);

        return Result<IReadOnlyList<LeaveBalanceAuditResponse>>.Success(
            rows.Select(LeaveBalanceAuditMapper.ToResponse).ToList());
    }
}
