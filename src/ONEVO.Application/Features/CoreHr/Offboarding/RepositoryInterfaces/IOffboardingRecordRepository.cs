using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

public interface IOffboardingRecordRepository
{
    /// <summary>The one record with status initiated/in_progress for this employee, or null.
    /// Used to enforce "at most one open offboarding per employee" and to drive resume/read-only
    /// banners on the frontend.</summary>
    Task<OffboardingRecord?> GetOpenByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<OffboardingRecord?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Most recent record by CreatedAt regardless of status - used by GET .../offboarding
    /// so a just-completed record is still visible (not only "open" ones).</summary>
    Task<OffboardingRecord?> GetLatestByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Batched latest-status lookup - avoids N+1 when listing many employees' offboarding
    /// overview. Absent key means the employee has no offboarding_records row at all.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetLatestStatusesByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    Task AddAsync(OffboardingRecord record, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
