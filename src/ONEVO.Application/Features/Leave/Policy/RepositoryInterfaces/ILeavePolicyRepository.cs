using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

public interface ILeavePolicyRepository
{
    Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default);

    Task<LeaveType?> GetLeaveTypeAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingPolicyId, CancellationToken ct = default);

    Task<LeavePolicy?> GetTrackedAsync(Guid tenantId, Guid policyId, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePolicy>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePolicyVersion>> ListVersionsTrackedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePolicyBlackoutPeriod>> ListBlackoutsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> versionIds, CancellationToken ct = default);

    /// <summary>Open (EffectiveTo null) assignments of every policy for one leave type.</summary>
    Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForLeaveTypeTrackedAsync(
        Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    /// <summary>Open assignments of the given policies.</summary>
    Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForPoliciesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default);

    /// <summary>The targets that do not exist (or are inactive) in the tenant.</summary>
    Task<IReadOnlyList<LeavePolicyAssignmentInput>> ListMissingTargetsAsync(
        Guid tenantId, IReadOnlyCollection<LeavePolicyAssignmentInput> targets, CancellationToken ct = default);

    Task<IReadOnlyDictionary<(string Kind, Guid Id), string>> GetTargetNamesAsync(
        Guid tenantId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetPolicyNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>True when the employee record belongs to the given user.</summary>
    Task<bool> IsUsersOwnEmployeeAsync(Guid tenantId, Guid employeeId, Guid userId, CancellationToken ct = default);

    void AddPolicy(LeavePolicy policy);

    void AddVersion(LeavePolicyVersion version, IReadOnlyList<LeavePolicyBlackoutPeriod> blackouts);

    void RemoveVersion(LeavePolicyVersion version);

    void AddAssignment(LeavePolicyAssignment assignment);

    void RemoveAssignment(LeavePolicyAssignment assignment);

    Task SaveChangesAsync(CancellationToken ct = default);
}
