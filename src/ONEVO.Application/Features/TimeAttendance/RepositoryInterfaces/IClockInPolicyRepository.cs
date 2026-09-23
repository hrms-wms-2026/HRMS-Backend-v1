using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IClockInPolicyRepository
{
    Task<IReadOnlyList<ClockInPolicy>> ListByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        bool includeInactive,
        CancellationToken ct = default);

    Task<ClockInPolicy?> GetByIdAsync(
        Guid tenantId, Guid policyId, CancellationToken ct = default);

    Task<ClockInPolicy?> GetByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid policyId, CancellationToken ct = default);

    /// <summary>Tracked fetch for mutations (includes late deduction rules).</summary>
    Task<ClockInPolicy?> GetTrackedByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid policyId, CancellationToken ct = default);

    Task<bool> HasOverlappingActiveScopeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string scopeType,
        Guid[]? departmentIds,
        Guid[]? positionIds,
        Guid[]? employeeIds,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? excludingPolicyId,
        CancellationToken ct = default);

    Task AddAsync(ClockInPolicy policy, CancellationToken ct = default);

    void Update(ClockInPolicy policy);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
