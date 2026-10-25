using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

/// <summary>Loads, in one round trip per table, everything <c>LeavePolicyResolver</c> needs to work
/// out which policy applies to each of the given employees on any date.</summary>
public interface ILeavePolicyResolutionRepository
{
    Task<LeavePolicyResolutionSnapshot> LoadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
}

public sealed record LeaveResolutionEmployee(Guid Id, string Name, Guid? LegalEntityId, Guid? DepartmentId);

/// <summary>A primary-employment position assignment (active or ended; planned and cancelled excluded).</summary>
public sealed record LeaveResolutionPosition(Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record LeaveResolutionDepartment(Guid Id, Guid? ParentDepartmentId);

/// <summary>An active policy whose leave type is active.</summary>
public sealed record LeaveResolutionPolicy(Guid Id, string Name, Guid LeaveTypeId);

public sealed record LeavePolicyResolutionSnapshot(
    IReadOnlyList<LeaveResolutionEmployee> Employees,
    IReadOnlyList<LeaveResolutionPosition> Positions,
    IReadOnlyList<LeaveResolutionDepartment> Departments,
    IReadOnlyList<LeaveResolutionPolicy> Policies,
    IReadOnlyList<LeavePolicyVersion> Versions,
    IReadOnlyList<LeavePolicyBlackoutPeriod> Blackouts,
    IReadOnlyList<LeavePolicyAssignment> Assignments,
    IReadOnlyDictionary<(string Kind, Guid Id), string> TargetNames);
