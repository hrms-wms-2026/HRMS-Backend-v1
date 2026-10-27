using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public interface IEmployeeClockInPolicyResolver
{
    /// <summary>The most specific active policy covering the employee on <paramref name="date"/>: employee > position
    /// (active primary assignment) > department > full company. Null when none applies.</summary>
    Task<ClockInPolicy?> ResolveAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, DateOnly date, CancellationToken ct);
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

        static ClockInPolicy? Latest(IEnumerable<ClockInPolicy> candidates) => candidates.OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();

        if (Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopeEmployee && p.EmployeeIds?.Contains(employeeId) == true)) is { } mine)
            return mine;
        if (policies.Any(p => p.ScopeType == ClockInPolicy.ScopePosition)
            && await _assignments.GetActivePrimaryAsync(tenantId, employeeId, ct) is { } seat
            && Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopePosition && p.PositionIds?.Contains(seat.PositionId) == true)) is { } position)
            return position;
        if (policies.Any(p => p.ScopeType == ClockInPolicy.ScopeDepartment)
            && (await _employees.GetByIdAsync(tenantId, employeeId, ct))?.DepartmentId is Guid departmentId
            && Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopeDepartment && p.DepartmentIds?.Contains(departmentId) == true)) is { } department)
            return department;
        return Latest(policies.Where(p => p.ScopeType == ClockInPolicy.ScopeFullCompany));
    }
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
