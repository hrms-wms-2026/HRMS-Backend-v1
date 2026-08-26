using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;

public class ListAllBalancesQueryHandler
    : IRequestHandler<ListAllBalancesQuery, Result<IReadOnlyList<LeaveBalanceResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyRepository _policies;

    public ListAllBalancesQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyRepository policies)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
        _policies = policies;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceResponse>>> Handle(
        ListAllBalancesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveBalanceResponse>>.Forbidden("Tenant context missing.");

        var rows = await _entitlements.ListRowsAsync(
            _currentUser.TenantId,
            new LeaveEntitlementListFilter(
                request.Year,
                null,
                null,
                request.LegalEntityId,
                request.DepartmentId,
                request.LeaveTypeId,
                request.EmploymentStatusId,
                request.Search),
            ct);

        var asOfDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime);
        return Result<IReadOnlyList<LeaveBalanceResponse>>.Success(
            await LeaveBalanceMapping.MapAsync(
                _policies, _currentUser.TenantId, request.Year, asOfDate, rows, ct));
    }
}
