using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.ListEntitlements;

public class ListEntitlementsQueryHandler
    : IRequestHandler<ListEntitlementsQuery, Result<IReadOnlyList<LeaveEntitlementResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly IEmployeeRepository _employees;
    private readonly ILeavePolicyRepository _policies;

    public ListEntitlementsQueryHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements,
        IEmployeeRepository employees,
        ILeavePolicyRepository policies)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
        _employees = employees;
        _policies = policies;
    }

    public async Task<Result<IReadOnlyList<LeaveEntitlementResponse>>> Handle(
        ListEntitlementsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveEntitlementResponse>>.Forbidden("Authentication required.");
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveEntitlementResponse>>.Forbidden("Tenant context missing.");

        var tenantId = _currentUser.TenantId;
        var asOfDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime);
        var rows = await _entitlements.ListRowsAsync(tenantId, new LeaveEntitlementListFilter(
            request.Year, null, null, request.LegalEntityId, request.DepartmentId, request.LeaveTypeId, null, request.Search), ct);

        var employeeIds = rows.Select(r => r.Entitlement.EmployeeId).Distinct().ToArray();
        var warnings = await _employees.ListLegalEntityChangeWarningsAsync(tenantId, employeeIds, request.Year, ct);
        var legalEntityIds = rows.Select(r => r.LegalEntityId).OfType<Guid>().Distinct().ToArray();
        var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, legalEntityIds, request.Year, ct);

        return Result<IReadOnlyList<LeaveEntitlementResponse>>.Success(rows.Select(row =>
        {
            var policy = row.LegalEntityId is { } legalEntityId && policies.TryGetValue(legalEntityId, out var match)
                ? match
                : null;
            return LeaveEntitlementMapper.ToResponse(
                row,
                warnings.GetValueOrDefault(row.Entitlement.EmployeeId),
                asOfDate,
                LeaveEntitlementPlanner.CarryExpiryFromPolicy(policy, row.Entitlement.LeaveTypeId, request.Year));
        }).ToList());
    }
}
