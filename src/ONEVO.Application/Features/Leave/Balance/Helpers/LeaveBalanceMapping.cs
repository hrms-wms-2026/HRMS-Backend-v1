using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;

namespace ONEVO.Application.Features.Leave.Balance.Helpers;

public static class LeaveBalanceMapping
{
    public static async Task<IReadOnlyList<LeaveBalanceResponse>> MapAsync(
        ILeavePolicyResolver policies,
        Guid tenantId,
        int year,
        DateOnly asOfDate,
        IReadOnlyList<LeaveEntitlementRow> rows,
        CancellationToken ct)
    {
        var resolved = await ResolveCarryPoliciesAsync(policies, tenantId, year, rows, ct);
        return rows.Select(row => LeaveEntitlementMapper.ToBalance(
            row,
            asOfDate,
            LeaveCarryExpiry.For(Find(resolved, row), year))).ToList();
    }

    public static Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>> ResolveCarryPoliciesAsync(
        ILeavePolicyResolver policies, Guid tenantId, int year, IReadOnlyList<LeaveEntitlementRow> rows, CancellationToken ct) =>
        policies.ResolveForEmployeesAsync(
            tenantId,
            rows.Select(r => r.Entitlement.EmployeeId).Distinct().ToArray(),
            LeaveCarryExpiry.ResolutionDate(year),
            ct);

    public static ResolvedLeavePolicy? Find(
        IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>> resolved, LeaveEntitlementRow row) =>
        resolved.TryGetValue(row.Entitlement.EmployeeId, out var forEmployee)
            ? forEmployee.FirstOrDefault(p => p.LeaveTypeId == row.Entitlement.LeaveTypeId)
            : null;
}
