using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

public interface ILeavePolicyRepository
{
    Task<IReadOnlyList<LeavePolicyAggregate>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<LeavePolicyAggregate?> GetAggregateByIdAsync(Guid tenantId, Guid leavePolicyId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeavePolicyId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveType>> ListActiveLeaveTypesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> leaveTypeIds, CancellationToken ct = default);

    Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePolicyLegalEntityConflict>> ListActiveAssignmentConflictsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default);

    Task AddAggregateWithReplacementAsync(
        LeavePolicy policy,
        IReadOnlyCollection<LeavePolicyLeaveType> leaveTypes,
        IReadOnlyCollection<LeavePolicyBlackoutPeriod> blackoutPeriods,
        IReadOnlyCollection<LeavePolicyLegalEntity> legalEntityAssignments,
        IReadOnlyCollection<Guid> legalEntityIdsToReplace,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, LeavePolicyAggregate>> ListActiveAggregatesByLegalEntityIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        int year,
        CancellationToken ct = default);
}

public record LeavePolicyAggregate(
    LeavePolicy Policy,
    IReadOnlyList<LeavePolicyLeaveTypeWithType> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriod> BlackoutPeriods,
    IReadOnlyList<LeavePolicyLegalEntityWithName> LegalEntities);

public record LeavePolicyLeaveTypeWithType(
    LeavePolicyLeaveType Rule,
    string LeaveTypeName,
    string LeaveTypeCode);

public record LeavePolicyLegalEntityWithName(
    LeavePolicyLegalEntity Assignment,
    string LegalEntityName,
    string StandardWorkingDaysJson);

public record LeavePolicyLegalEntityConflict(
    Guid LegalEntityId,
    string LegalEntityName,
    Guid ActivePolicyId,
    string ActivePolicyName);
