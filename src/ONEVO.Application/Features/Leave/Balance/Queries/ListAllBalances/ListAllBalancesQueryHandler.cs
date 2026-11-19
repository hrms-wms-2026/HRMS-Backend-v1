using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;

public class ListAllBalancesQueryHandler
    : IRequestHandler<ListAllBalancesQuery, Result<IReadOnlyList<LeaveBalanceResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyResolver _policies;
    private readonly IEmployeeRepository? _employees;
    private readonly ILeaveTypeRepository? _leaveTypes;

    public ListAllBalancesQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyResolver policies,
        IEmployeeRepository? employees = null,
        ILeaveTypeRepository? leaveTypes = null)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
        _policies = policies;
        _employees = employees;
        _leaveTypes = leaveTypes;
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
        if (_employees is not null && _leaveTypes is not null)
        {
            var people = await _employees.ListActiveByLegalEntityAsync(_currentUser.TenantId, request.LegalEntityId, ct);
            var ids = people
                .Where(e => request.DepartmentId is null || e.DepartmentId == request.DepartmentId)
                .Where(e => request.EmploymentStatusId is null || e.EmploymentStatusId == request.EmploymentStatusId)
                .Where(e => string.IsNullOrWhiteSpace(request.Search)
                    || e.EmployeeNumber.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
                    || e.FirstName.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
                    || e.LastName.Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Id).ToList();
            rows = await LeaveBalanceMapping.IncludeResolvedZeroRowsAsync(
                _policies, _leaveTypes, _employees, _currentUser.TenantId, request.Year,
                asOfDate, ids, rows, ct);
            if (request.LeaveTypeId is { } leaveTypeId)
                rows = rows.Where(r => r.Entitlement.LeaveTypeId == leaveTypeId).ToList();
        }
        return Result<IReadOnlyList<LeaveBalanceResponse>>.Success(
            await LeaveBalanceMapping.MapAsync(
                _policies, _currentUser.TenantId, request.Year, asOfDate, rows, ct));
    }
}
