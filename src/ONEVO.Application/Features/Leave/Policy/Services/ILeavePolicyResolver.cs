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
}
