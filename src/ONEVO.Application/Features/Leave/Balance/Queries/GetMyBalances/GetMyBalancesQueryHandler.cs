using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;

public class GetMyBalancesQueryHandler
    : IRequestHandler<GetMyBalancesQuery, Result<IReadOnlyList<LeaveBalanceResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyRepository _policies;

    public GetMyBalancesQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IEmployeeRepository employees,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyRepository policies)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _employees = employees;
        _entitlements = entitlements;
        _policies = policies;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceResponse>>> Handle(
        GetMyBalancesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.NotFound(LeaveEntitlementMessages.NoEmployeeRecord);

        var rows = await _entitlements.ListRowsAsync(
            _currentUser.TenantId,
            new LeaveEntitlementListFilter(request.Year, employee.Id, null, null, null, null, null, null),
            ct);

        var asOfDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime);
        return Result<IReadOnlyList<LeaveBalanceResponse>>.Success(
            await LeaveBalanceMapping.MapAsync(_policies, _currentUser.TenantId, request.Year, asOfDate, rows, ct));
    }
}
