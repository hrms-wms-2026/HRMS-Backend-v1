using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;

public interface IExceptionRepository
{
    Task AddAsync(MonitoringException exception, CancellationToken ct);

    /// <summary>Anti-duplicate check before the detection job creates a new case.</summary>
    Task<bool> HasOpenOrEscalatedAsync(Guid tenantId, Guid employeeId, ExceptionType type, CancellationToken ct);

    /// <summary>Open exceptions older than the escalation threshold, for the nightly escalation sweep.</summary>
    Task<IReadOnlyList<MonitoringException>> GetStaleOpenAsync(Guid tenantId, DateTimeOffset olderThan, CancellationToken ct);

    /// <summary>All tenants' stale-open exceptions in one pass - the job iterates tenants itself, but a
    /// tenant-scoped query needs RLS tenant context set first; this is the System-mode variant used only
    /// by the background job's own tenant-switching loop (see Task 4).</summary>
    Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetActiveTenantEmployeeKeysAsync(
        DateTimeOffset sinceUtc, CancellationToken ct);

    Task<MonitoringException?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<MonitoringException>> GetListAsync(
        Guid tenantId, ExceptionStatus? status, ExceptionType? type, int page, int pageSize, CancellationToken ct);

    Task<int> GetListTotalCountAsync(Guid tenantId, ExceptionStatus? status, ExceptionType? type, CancellationToken ct);

    void Update(MonitoringException exception);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
