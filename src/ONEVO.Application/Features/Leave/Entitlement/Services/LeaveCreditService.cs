using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.Services;

public sealed class LeaveCreditService : ILeaveCreditService
{
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeavePolicyResolver _resolver;

    public LeaveCreditService(
        IEmployeeRepository employees,
        ILeaveEntitlementRepository entitlements,
        ILeavePolicyResolver resolver)
    {
        _employees = employees;
        _entitlements = entitlements;
        _resolver = resolver;
    }

    public async Task<IReadOnlyList<LeaveCreditOutcome>> CreditAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        DateOnly asOf,
        bool dryRun,
        Guid? actorId,
        CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return [];

        try
        {
            return await CreditOnceAsync(tenantId, employeeIds, year, asOf, dryRun, actorId, ct);
        }
        catch (ConcurrencyConflictException) when (!dryRun)
        {
            return await CreditOnceAsync(tenantId, employeeIds, year, asOf, false, actorId, ct);
        }
    }

    private async Task<IReadOnlyList<LeaveCreditOutcome>> CreditOnceAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        DateOnly asOf,
        bool dryRun,
        Guid? actorId,
        CancellationToken ct)
    {
        var employees = await _employees.ListByIdsAsync(tenantId, employeeIds, ct);
        var existing = (dryRun
                ? await _entitlements.ListExistingAsync(tenantId, year, employeeIds, ct)
                : await _entitlements.ListTrackedExistingAsync(tenantId, year, employeeIds, ct))
            .ToDictionary(e => (e.EmployeeId, e.LeaveTypeId));
        var audits = await _entitlements.ListAuditsAsync(tenantId, employeeIds, year, ct);
        var auditsByKey = audits
            .Where(a => a.ChangeType == LeaveBalanceChangeTypes.Accrual && a.CoversFromMonth is not null)
            .GroupBy(a => (a.EmployeeId, a.LeaveTypeId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LeaveBalanceAudit>)g.ToList());

        var monthSnapshots = new Dictionary<int, IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>>();
        for (var month = 1; month <= 12; month++)
        {
            var date = new DateOnly(year, month, 1);
            monthSnapshots[month] = await _resolver.ResolveForEmployeesAsync(tenantId, employeeIds, date, ct);
        }
        var asOfSnapshot = await _resolver.ResolveForEmployeesAsync(tenantId, employeeIds, asOf, ct);

        var outcomes = new List<LeaveCreditOutcome>();
        var writes = new List<LeaveEntitlementWriteSet>();
        foreach (var employee in employees.Values)
        {
            var current = asOfSnapshot.GetValueOrDefault(employee.Id) ?? [];
            var typeIds = existing.Keys.Where(k => k.EmployeeId == employee.Id).Select(k => k.LeaveTypeId)
                .Concat(current.Select(p => p.LeaveTypeId))
                .Concat(monthSnapshots.Values.SelectMany(s => s.GetValueOrDefault(employee.Id) ?? []).Select(p => p.LeaveTypeId))
                .Distinct()
                .ToList();

            foreach (var leaveTypeId in typeIds)
            {
                var key = (employee.Id, leaveTypeId);
                existing.TryGetValue(key, out var entitlement);
                if (entitlement?.Source == LeaveEntitlementSources.Manual)
                {
                    outcomes.Add(new(employee.Id, leaveTypeId, 0m, entitlement.TotalHours, "Manual entitlement"));
                    continue;
                }

                var policies = BuildMonthPolicies(employee.Id, leaveTypeId, asOf, monthSnapshots, current);
                if (policies.Count == 0)
                {
                    outcomes.Add(new(employee.Id, leaveTypeId, 0m, 0m, "No policy assigned"));
                    continue;
                }

                var due = LeaveCreditCalculator.DueMonths(new LeaveCreditInput(
                    year, employee.HireDate, employee.ProbationEndDate, asOf, policies));
                if (due.Count == 0)
                {
                    outcomes.Add(new(employee.Id, leaveTypeId, 0m, 0m, "Not eligible yet"));
                    continue;
                }

                var credited = CreditedByMonth(auditsByKey.GetValueOrDefault(key) ?? []);
                var deltas = due
                    .Where(value => !(value.Month < asOf.Month && credited.ContainsKey(value.Month)))
                    .Select(value => new MonthDelta(value, value.Hours - credited.GetValueOrDefault(value.Month)))
                    .Where(x => Round(x.Delta) != 0m)
                    .ToList();
                var hoursWritten = Round(deltas.Sum(x => x.Delta));
                var expected = due.Sum(x => x.Hours);

                if (!dryRun && deltas.Count > 0)
                {
                    entitlement ??= NewEntitlement(tenantId, employee.Id, leaveTypeId, year);
                    var writeAudits = BuildAuditRows(
                        tenantId, employee.Id, leaveTypeId, year, entitlement, deltas, actorId, asOf);
                    entitlement.TotalHours += writeAudits.Sum(a => a.HoursChanged);
                    var currentValue = due.Where(x => x.Month <= asOf.Month).MaxBy(x => x.Month) ?? due[^1];
                    entitlement.LeavePolicyId = currentValue.PolicyId;
                    entitlement.LeavePolicyVersionId = currentValue.VersionId;
                    entitlement.UpdatedAt = DateTimeOffset.UtcNow;
                    writes.Add(new LeaveEntitlementWriteSet(entitlement, writeAudits));
                }

                outcomes.Add(new(employee.Id, leaveTypeId, hoursWritten, expected, null));
            }
        }

        if (!dryRun && writes.Count > 0)
            await _entitlements.SaveCreditChangesAsync(writes, ct);
        return outcomes;
    }

    public async Task CloseYearAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        int year,
        Guid? actorId,
        CancellationToken ct)
    {
        var previous = await _entitlements.ListExistingAsync(tenantId, year, employeeIds, ct);
        var next = (await _entitlements.ListTrackedExistingAsync(tenantId, year + 1, employeeIds, ct))
            .ToDictionary(e => (e.EmployeeId, e.LeaveTypeId));
        var nextAudits = await _entitlements.ListAuditsAsync(tenantId, employeeIds, year + 1, ct);
        var writes = new List<LeaveEntitlementWriteSet>();

        foreach (var source in previous)
        {
            if (nextAudits.Any(a => a.EmployeeId == source.EmployeeId && a.LeaveTypeId == source.LeaveTypeId
                                    && a.ChangeType == LeaveBalanceChangeTypes.CarryForward))
                continue;

            var resolved = await _resolver.ResolveAsync(
                tenantId, source.EmployeeId, source.LeaveTypeId, new DateOnly(year, 12, 31), ct);
            if (resolved is null)
                continue;

            var remaining = source.TotalHours + source.CarriedForwardHours - source.UsedHours;
            var carried = remaining < 0m
                ? remaining
                : Math.Min(remaining, resolved.Version.CarryForwardCapHours ?? remaining);
            var forfeited = remaining > 0m ? remaining - carried : 0m;
            var key = (source.EmployeeId, source.LeaveTypeId);
            if (!next.TryGetValue(key, out var target))
            {
                target = NewEntitlement(tenantId, source.EmployeeId, source.LeaveTypeId, year + 1);
                next[key] = target;
            }
            target.CarriedForwardHours = carried;
            target.LeavePolicyId = resolved.PolicyId;
            target.LeavePolicyVersionId = resolved.Version.Id;
            var rows = new List<LeaveBalanceAudit>
            {
                Audit(tenantId, source.EmployeeId, source.LeaveTypeId, year + 1,
                    LeaveBalanceChangeTypes.CarryForward, remaining, remaining,
                    "Carry forward from previous leave year", actorId, resolved.PolicyId, resolved.Version.Id)
            };
            if (forfeited > 0m)
            {
                rows.Add(Audit(tenantId, source.EmployeeId, source.LeaveTypeId, year + 1,
                    LeaveBalanceChangeTypes.Forfeiture, -forfeited, carried,
                    "Carry-forward cap applied", actorId, resolved.PolicyId, resolved.Version.Id));
            }
            writes.Add(new LeaveEntitlementWriteSet(target, rows));
        }

        if (writes.Count > 0)
            await _entitlements.SaveCreditChangesAsync(writes, ct);
    }

    public async Task ExpireCarryAsync(Guid tenantId, int year, DateOnly asOf, CancellationToken ct)
    {
        var employeeIds = (await _employees.ListActiveByLegalEntityAsync(tenantId, null, ct)).Select(e => e.Id).ToList();
        var entitlements = await _entitlements.ListTrackedExistingAsync(tenantId, year, employeeIds, ct);
        var audits = await _entitlements.ListAuditsAsync(tenantId, employeeIds, year, ct);
        var writes = new List<LeaveEntitlementWriteSet>();
        foreach (var entitlement in entitlements.Where(e => e.CarriedForwardHours > 0m))
        {
            if (audits.Any(a => a.EmployeeId == entitlement.EmployeeId && a.LeaveTypeId == entitlement.LeaveTypeId
                                && a.ChangeType == LeaveBalanceChangeTypes.Forfeiture
                                && a.Reason == "Carried hours expired"))
                continue;
            var policy = await _resolver.ResolveAsync(
                tenantId, entitlement.EmployeeId, entitlement.LeaveTypeId, new DateOnly(year, 1, 1), ct);
            var expiry = policy?.Version.CarryForwardExpiryMonths is { } months
                ? new DateOnly(year, 1, 1).AddMonths(months)
                : (DateOnly?)null;
            if (expiry is null || expiry > asOf)
                continue;
            var expired = entitlement.CarriedForwardHours;
            entitlement.CarriedForwardHours = 0m;
            writes.Add(new LeaveEntitlementWriteSet(entitlement,
            [
                Audit(tenantId, entitlement.EmployeeId, entitlement.LeaveTypeId, year,
                    LeaveBalanceChangeTypes.Forfeiture, -expired,
                    LeaveEntitlementMapper.Remaining(entitlement), "Carried hours expired", null,
                    policy?.PolicyId, policy?.Version.Id)
            ]));
        }
        if (writes.Count > 0)
            await _entitlements.SaveCreditChangesAsync(writes, ct);
    }

    private static List<LeaveMonthPolicy> BuildMonthPolicies(
        Guid employeeId,
        Guid leaveTypeId,
        DateOnly asOf,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>> snapshots,
        IReadOnlyList<ResolvedLeavePolicy> current)
    {
        var currentPolicy = current.FirstOrDefault(p => p.LeaveTypeId == leaveTypeId);
        var result = new List<LeaveMonthPolicy>();
        for (var month = 1; month <= 12; month++)
        {
            var historical = (snapshots[month].GetValueOrDefault(employeeId) ?? [])
                .FirstOrDefault(p => p.LeaveTypeId == leaveTypeId);
            var selected = month < asOf.Month
                ? historical
                : currentPolicy?.Version.AmountPeriod == LeaveAmountPeriods.Month ? historical : currentPolicy;
            if (selected is not null)
                result.Add(new LeaveMonthPolicy(month, selected.Version, selected.PolicyId));
        }
        return result;
    }

    private static Dictionary<int, decimal> CreditedByMonth(IReadOnlyList<LeaveBalanceAudit> audits)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var row in audits)
        {
            var count = row.CoversToMonth!.Value - row.CoversFromMonth!.Value + 1;
            var perMonth = row.HoursChanged / count;
            for (var month = row.CoversFromMonth.Value; month <= row.CoversToMonth.Value; month++)
                result[month] = result.GetValueOrDefault(month) + perMonth;
        }
        return result;
    }

    private static IReadOnlyList<LeaveBalanceAudit> BuildAuditRows(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int year,
        LeaveEntitlement entitlement,
        IReadOnlyList<MonthDelta> deltas,
        Guid? actorId,
        DateOnly asOf)
    {
        var result = new List<LeaveBalanceAudit>();
        var running = LeaveEntitlementMapper.Remaining(entitlement);
        foreach (var group in Group(deltas))
        {
            var amount = Round(group.Sum(x => x.Delta));
            running += amount;
            var first = group[0];
            var last = group[^1];
            var revaluation = group.Any(x => x.Value.Month >= asOf.Month && x.Delta != x.Value.Hours);
            result.Add(new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Accrual, HoursChanged = amount, BalanceAfter = running,
                Reason = revaluation ? "Policy change" : first.Value.Month == last.Value.Month
                    ? $"Credit for {new DateOnly(year, first.Value.Month, 1):MMM yyyy}"
                    : "Yearly credit",
                LeavePolicyId = first.Value.PolicyId, LeavePolicyVersionId = first.Value.VersionId,
                Year = year, CoversFromMonth = first.Value.Month, CoversToMonth = last.Value.Month,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = actorId
            });
        }
        return result;
    }

    private static IReadOnlyList<List<MonthDelta>> Group(IReadOnlyList<MonthDelta> values)
    {
        var groups = new List<List<MonthDelta>>();
        List<MonthDelta>? current = null;
        foreach (var value in values.OrderBy(x => x.Value.Month))
        {
            if (current is null || value.Value.Month != current[^1].Value.Month + 1
                || value.Value.VersionId != current[^1].Value.VersionId
                || Math.Sign(value.Delta) != Math.Sign(current[^1].Delta))
            {
                current = [value];
                groups.Add(current);
            }
            else
            {
                current.Add(value);
            }
        }
        return groups;
    }

    private static LeaveEntitlement NewEntitlement(Guid tenantId, Guid employeeId, Guid leaveTypeId, int year) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
        Year = year, Source = LeaveEntitlementSources.Auto, CreatedAt = DateTimeOffset.UtcNow
    };

    private static LeaveBalanceAudit Audit(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, int year, string type,
        decimal changed, decimal balanceAfter, string reason, Guid? actorId,
        Guid? policyId, Guid? versionId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
        ChangeType = type, HoursChanged = Round(changed), BalanceAfter = Round(balanceAfter), Reason = reason,
        LeavePolicyId = policyId, LeavePolicyVersionId = versionId, Year = year,
        CreatedAt = DateTimeOffset.UtcNow, CreatedBy = actorId
    };

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record MonthDelta(LeaveMonthValue Value, decimal Delta);
}
