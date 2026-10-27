using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public interface IEmployeeClockInPolicyResolver
{
    /// <summary>The most specific active policy covering the employee on <paramref name="date"/>: employee > position
    /// (active primary assignment) > department > full company. Null when none applies.</summary>
    Task<ClockInPolicy?> ResolveAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, DateOnly date, CancellationToken ct);

    /// <summary>Each employee's late/early threshold minutes for <paramref name="date"/>, in one batch.</summary>
    Task<IReadOnlyDictionary<Guid, int>> ResolveThresholdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Employee> employees, DateOnly date, CancellationToken ct);
}

public sealed class EmployeeClockInPolicyResolver : IEmployeeClockInPolicyResolver
{
    private readonly IClockInPolicyRepository _policies;
    private readonly IEmployeeRepository _employees;
    private readonly IPositionAssignmentRepository _assignments;

    public EmployeeClockInPolicyResolver(
        IClockInPolicyRepository policies, IEmployeeRepository employees, IPositionAssignmentRepository assignments)
    {
        _policies = policies;
        _employees = employees;
        _assignments = assignments;
    }

    public async Task<ClockInPolicy?> ResolveAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, DateOnly date, CancellationToken ct)
    {
        var policies = await _policies.ListActiveOnDateAsync(tenantId, legalEntityId, date, ct);
        if (policies.Count == 0)
            return null;

        if (Pick(policies, ClockInPolicy.ScopeEmployee, p => p.EmployeeIds, employeeId) is { } mine)
            return mine;
        if (policies.Any(p => p.ScopeType == ClockInPolicy.ScopePosition)
            && await _assignments.GetActivePrimaryAsync(tenantId, employeeId, ct) is { } seat
            && Pick(policies, ClockInPolicy.ScopePosition, p => p.PositionIds, seat.PositionId) is { } position)
            return position;
        if (policies.Any(p => p.ScopeType == ClockInPolicy.ScopeDepartment)
            && (await _employees.GetByIdAsync(tenantId, employeeId, ct))?.DepartmentId is Guid departmentId
            && Pick(policies, ClockInPolicy.ScopeDepartment, p => p.DepartmentIds, departmentId) is { } department)
            return department;
        return Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopeFullCompany));
    }

    /// <summary>Batched ResolveAsync reduced to each employee's late/early threshold (ClockInPolicyRules.ThresholdMinutes),
    /// keyed by employee id: one policy read and one position read for the whole set, same precedence as ResolveAsync.
    /// The passed employees' DepartmentId is used as-is.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> ResolveThresholdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Employee> employees, DateOnly date, CancellationToken ct)
    {
        var result = new Dictionary<Guid, int>(employees.Count);
        if (employees.Count == 0)
            return result;

        var policies = await _policies.ListActiveOnDateAsync(tenantId, legalEntityId, date, ct);
        if (policies.Count == 0)
        {
            foreach (var employee in employees) result[employee.Id] = 0;
            return result;
        }

        var seats = policies.Any(p => p.ScopeType == ClockInPolicy.ScopePosition)
            ? await _assignments.GetActivePrimaryByEmployeeIdsAsync(tenantId, employees.Select(e => e.Id).ToList(), ct)
            : new Dictionary<Guid, PositionAssignment>();
        var company = Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopeFullCompany));

        foreach (var employee in employees)
        {
            var policy = Pick(policies, ClockInPolicy.ScopeEmployee, p => p.EmployeeIds, employee.Id)
                ?? (seats.TryGetValue(employee.Id, out var seat) ? Pick(policies, ClockInPolicy.ScopePosition, p => p.PositionIds, seat.PositionId) : null)
                ?? (employee.DepartmentId is Guid departmentId ? Pick(policies, ClockInPolicy.ScopeDepartment, p => p.DepartmentIds, departmentId) : null)
                ?? company;
            result[employee.Id] = ClockInPolicyRules.ThresholdMinutes(policy);
        }

        return result;
    }

    private static ClockInPolicy? Latest(IEnumerable<ClockInPolicy> candidates) => candidates.OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();

    private static ClockInPolicy? Pick(IEnumerable<ClockInPolicy> policies, string scope, Func<ClockInPolicy, IEnumerable<Guid>?> ids, Guid id) =>
        Latest(policies.Where(p => p.ScopeType == scope && ids(p)?.Contains(id) == true));
}

/// <summary>The late/early deduction rows of a policy: the threshold is the first row; a lateness of N minutes is
/// charged by the highest row whose minutes are at most N.</summary>
public static class ClockInPolicyRules
{
    public static IReadOnlyList<ClockInLateDeductionRule> Active(ClockInPolicy? policy)
        => policy is null ? [] : policy.LateDeductionRules.Where(r => r.IsActive).OrderBy(r => r.LateArrivalMinute).ToList();

    public static int ThresholdMinutes(ClockInPolicy? policy) => Active(policy).FirstOrDefault()?.LateArrivalMinute ?? 0;

    public static ClockInLateDeductionRule? Match(ClockInPolicy? policy, int minutes)
        => Active(policy).LastOrDefault(r => r.LateArrivalMinute <= minutes);

    public static bool SameRows(
        IEnumerable<(int Minutes, decimal Multiplier, Guid TypeId)> before, IEnumerable<(int Minutes, decimal Multiplier, Guid TypeId)> after)
        => before.OrderBy(r => r.Minutes).ThenBy(r => r.TypeId).SequenceEqual(after.OrderBy(r => r.Minutes).ThenBy(r => r.TypeId));
}
