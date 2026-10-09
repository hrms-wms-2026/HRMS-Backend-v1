using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;

/// <summary>List filter. <see cref="EmployeeIds"/> null means every employee in the tenant (HR);
/// an empty collection means none. <see cref="ExcludeEmployeeId"/> keeps the reviewer's own
/// cases out of their list.</summary>
public sealed record ExceptionListFilter(
    ExceptionStatus? Status,
    ExceptionType? Type,
    IReadOnlyCollection<Guid>? EmployeeIds,
    Guid? ExcludeEmployeeId,
    IReadOnlyCollection<ExceptionStatus>? Statuses = null,
    bool ActiveOnly = false);

public interface IExceptionRepository
{
    Task AddAsync(MonitoringException exception, CancellationToken ct);

    /// <summary>Anti-duplicate check before the detection job creates a new case: true while a case
    /// of this type is still open, acknowledged (being worked) or escalated.</summary>
    Task<bool> HasUnresolvedAsync(Guid tenantId, Guid employeeId, ExceptionType type, CancellationToken ct);

    /// <summary>As <see cref="HasUnresolvedAsync"/>, but only cases detected at or after
    /// <paramref name="sinceUtc"/> - real-time identity cases dedupe per day, so an old case left
    /// acknowledged can't hide a new impostor on a later day.</summary>
    Task<bool> HasUnresolvedSinceAsync(
        Guid tenantId, Guid employeeId, ExceptionType type, DateTimeOffset sinceUtc, CancellationToken ct);

    /// <summary>Open exceptions older than the escalation threshold, for the nightly escalation sweep.
    /// Acknowledged cases are deliberately left alone - a reviewer already owns them.</summary>
    Task<IReadOnlyList<MonitoringException>> GetStaleOpenAsync(Guid tenantId, DateTimeOffset olderThan, CancellationToken ct);

    /// <summary>All tenants' stale-open exceptions in one pass - the job iterates tenants itself, but a
    /// tenant-scoped query needs RLS tenant context set first; this is the System-mode variant used only
    /// by the background job's own tenant-switching loop (see Task 4).</summary>
    Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetActiveTenantEmployeeKeysAsync(
        DateTimeOffset sinceUtc, CancellationToken ct);

    Task<MonitoringException?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>One employee's cases of a type detected in [from, to), oldest first.</summary>
    Task<IReadOnlyList<MonitoringException>> ListForEmployeeInRangeAsync(
        Guid tenantId, Guid employeeId, ExceptionType type, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Exception cases for this employee with fromUtc &lt;= DetectedAt &lt; toUtcExclusive, any status.</summary>
    Task<int> CountDetectedInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    /// <summary>The cases <see cref="CountDetectedInRangeAsync"/> counts, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<MonitoringException>> ListDetectedInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct);

    /// <summary>Distinct employees who have any exception case in the tenant - the candidate set a
    /// manager's approver scope is resolved over before listing.</summary>
    Task<IReadOnlyList<Guid>> ListEmployeeIdsWithExceptionsAsync(Guid tenantId, CancellationToken ct);

    Task<IReadOnlyList<MonitoringException>> GetListAsync(
        Guid tenantId, ExceptionListFilter filter, int page, int pageSize, CancellationToken ct);

    Task<int> GetListTotalCountAsync(Guid tenantId, ExceptionListFilter filter, CancellationToken ct);

    void Update(MonitoringException exception);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
