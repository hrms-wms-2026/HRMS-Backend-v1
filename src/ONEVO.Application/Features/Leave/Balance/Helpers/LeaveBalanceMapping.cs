using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Balance.Helpers;

public static class LeaveBalanceMapping
{
    public static async Task<IReadOnlyList<LeaveEntitlementRow>> IncludeResolvedZeroRowsAsync(
        ILeavePolicyResolver policies,
        ILeaveTypeRepository leaveTypes,
        IEmployeeRepository employees,
        Guid tenantId,
        int year,
        DateOnly asOfDate,
        IReadOnlyCollection<Guid> employeeIds,
        IReadOnlyList<LeaveEntitlementRow> rows,
        CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return rows;

        var resolved = await policies.ResolveForEmployeesAsync(tenantId, employeeIds, asOfDate, ct);
        var people = await employees.ListByIdsAsync(tenantId, employeeIds, ct);
        var types = (await leaveTypes.ListAsync(tenantId, includeInactive: false, ct)).ToDictionary(t => t.Id);
        var keys = rows.Select(r => (r.Entitlement.EmployeeId, r.Entitlement.LeaveTypeId)).ToHashSet();
        var expanded = rows.ToList();
        foreach (var (employeeId, employeePolicies) in resolved)
        {
            if (!people.TryGetValue(employeeId, out var employee))
                continue;
            foreach (var policy in employeePolicies.Where(p => !keys.Contains((employeeId, p.LeaveTypeId))))
            {
                if (!types.TryGetValue(policy.LeaveTypeId, out var type))
                    continue;
                expanded.Add(new LeaveEntitlementRow(
                    new LeaveEntitlement
                    {
                        Id = Guid.Empty, TenantId = tenantId, EmployeeId = employeeId,
                        LeaveTypeId = policy.LeaveTypeId, Year = year, Source = LeaveEntitlementSources.Auto,
                        LeavePolicyId = policy.PolicyId, LeavePolicyVersionId = policy.Version.Id
                    },
                    employee.EmployeeNumber,
                    $"{employee.FirstName} {employee.LastName}".Trim(),
                    employee.DepartmentId, null, employee.LegalEntityId, null,
                    type.Name, type.Code, 0m));
            }
        }
        return expanded;
    }

    public static async Task<IReadOnlyList<LeaveBalanceResponse>> MapAsync(
        ILeavePolicyResolver policies,
        Guid tenantId,
        int year,
        DateOnly asOfDate,
        IReadOnlyList<LeaveEntitlementRow> rows,
        CancellationToken ct)
    {
        var resolved = await ResolveCarryPoliciesAsync(policies, tenantId, year, rows, ct);
        var current = asOfDate == LeaveCarryExpiry.ResolutionDate(year)
            ? resolved
            : await policies.ResolveForEmployeesAsync(
                tenantId, rows.Select(r => r.Entitlement.EmployeeId).Distinct().ToArray(), asOfDate, ct);
        return rows.Select(row => LeaveEntitlementMapper.ToBalance(
            row,
            asOfDate,
            LeaveCarryExpiry.For(Find(resolved, row), year),
            Find(current, row))).ToList();
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
