using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Services;

/// <summary>The policy version that applies to one employee for one leave type on a date, and the
/// level (employee, position, department, company) it came from.</summary>
public sealed record ResolvedLeavePolicy(
    Guid LeaveTypeId,
    Guid PolicyId,
    string PolicyName,
    LeavePolicyVersion Version,
    IReadOnlyList<LeavePolicyBlackoutPeriod> Blackouts,
    string SourceKind,
    Guid SourceTargetId,
    string SourceTargetName);

/// <summary>One (employee, leave type, date) question for <see cref="ILeavePolicyResolver.ResolveManyAsync"/>.</summary>
public sealed record LeavePolicyLookup(Guid EmployeeId, Guid LeaveTypeId, DateOnly AsOf);

/// <summary>Per leave type the most specific assignment wins: the employee, then their primary
/// position, then their department (walking up parent departments, nearest first), then their
/// company. Only assignments and versions in force on the date count.</summary>
public interface ILeavePolicyResolver
{
    Task<ResolvedLeavePolicy?> ResolveAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly asOf, CancellationToken ct);

    Task<IReadOnlyList<ResolvedLeavePolicy>> ResolveAllAsync(
        Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>> ResolveForEmployeesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly asOf, CancellationToken ct);

    /// <summary><see cref="ResolveAsync"/> for several lookups (any employees, leave types and dates) over one snapshot
    /// read. Every lookup is a key of the result; it maps to null where <see cref="ResolveAsync"/> would.</summary>
    Task<IReadOnlyDictionary<LeavePolicyLookup, ResolvedLeavePolicy?>> ResolveManyAsync(
        Guid tenantId, IReadOnlyCollection<LeavePolicyLookup> lookups, CancellationToken ct);
}
