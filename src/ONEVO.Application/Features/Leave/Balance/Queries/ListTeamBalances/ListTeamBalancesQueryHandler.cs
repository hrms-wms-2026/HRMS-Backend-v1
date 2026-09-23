using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListTeamBalances;

public class ListTeamBalancesQueryHandler
    : IRequestHandler<ListTeamBalancesQuery, Result<IReadOnlyList<LeaveBalanceResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeHierarchyClosureRepository _hierarchy;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyRepository _policies;

    public ListTeamBalancesQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IEmployeeRepository employees,
        IEmployeeHierarchyClosureRepository hierarchy,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyRepository policies)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _employees = employees;
        _hierarchy = hierarchy;
        _entitlements = entitlements;
        _policies = policies;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceResponse>>> Handle(
        ListTeamBalancesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var manager = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (manager is null)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.NotFound(LeaveEntitlementMessages.NoEmployeeRecord);

        var reportIds = await _hierarchy.GetDescendantEmployeeIdsAsync(tenantId, manager.Id, ct);
        var rows = await _entitlements.ListRowsAsync(
            tenantId,
            new LeaveEntitlementListFilter(
                request.Year, null, reportIds, null, request.DepartmentId, request.LeaveTypeId, null, request.Search),
            ct);

        var asOfDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime);
        return Result<IReadOnlyList<LeaveBalanceResponse>>.Success(
            await LeaveBalanceMapping.MapAsync(_policies, tenantId, request.Year, asOfDate, rows, ct));
    }
}
