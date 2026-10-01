using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Exceptions;

public class EfExceptionRepository : IExceptionRepository
{
    private readonly ApplicationDbContext _db;

    public EfExceptionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(MonitoringException exception, CancellationToken ct)
        => await _db.Exceptions.AddAsync(exception, ct);

    public async Task<bool> HasUnresolvedAsync(
        Guid tenantId, Guid employeeId, ExceptionType type, CancellationToken ct) =>
        await _db.Exceptions.AsNoTracking().AnyAsync(
            e => e.TenantId == tenantId && e.EmployeeId == employeeId && e.Type == type
                 && e.Status != ExceptionStatus.Resolved, ct);

    public async Task<bool> HasUnresolvedSinceAsync(
        Guid tenantId, Guid employeeId, ExceptionType type, DateTimeOffset sinceUtc, CancellationToken ct) =>
        await _db.Exceptions.AsNoTracking().AnyAsync(
            e => e.TenantId == tenantId && e.EmployeeId == employeeId && e.Type == type
                 && e.Status != ExceptionStatus.Resolved && e.DetectedAt >= sinceUtc, ct);

    public async Task<IReadOnlyList<MonitoringException>> GetStaleOpenAsync(
        Guid tenantId, DateTimeOffset olderThan, CancellationToken ct) =>
        await _db.Exceptions
            .Where(e => e.TenantId == tenantId && e.Status == ExceptionStatus.Open && e.DetectedAt < olderThan)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetActiveTenantEmployeeKeysAsync(
        DateTimeOffset sinceUtc, CancellationToken ct)
    {
        var rows = await _db.ActivityDailySummaries
            .AsNoTracking()
            .Where(s => s.CreatedAt >= sinceUtc)
            .Select(s => new { s.TenantId, s.EmployeeId })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(r => (r.TenantId, r.EmployeeId)).ToList();
    }

    public async Task<MonitoringException?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        await _db.Exceptions.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public Task<int> CountDetectedInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct) =>
        _db.Exceptions.AsNoTracking().CountAsync(
            e => e.TenantId == tenantId && e.EmployeeId == employeeId
                 && e.DetectedAt >= fromUtc && e.DetectedAt < toUtcExclusive, ct);

    public async Task<IReadOnlyList<Guid>> ListEmployeeIdsWithExceptionsAsync(Guid tenantId, CancellationToken ct) =>
        await _db.Exceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MonitoringException>> GetListAsync(
        Guid tenantId, ExceptionListFilter filter, int page, int pageSize, CancellationToken ct) =>
        await Filtered(tenantId, filter)
            .OrderByDescending(e => e.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<int> GetListTotalCountAsync(Guid tenantId, ExceptionListFilter filter, CancellationToken ct) =>
        await Filtered(tenantId, filter).CountAsync(ct);

    private IQueryable<MonitoringException> Filtered(Guid tenantId, ExceptionListFilter filter)
    {
        var query = _db.Exceptions.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (filter.Status.HasValue) query = query.Where(e => e.Status == filter.Status.Value);
        if (filter.Type.HasValue) query = query.Where(e => e.Type == filter.Type.Value);
        if (filter.EmployeeIds is not null)
        {
            var ids = filter.EmployeeIds.ToList();
            query = query.Where(e => ids.Contains(e.EmployeeId));
        }
        if (filter.ExcludeEmployeeId is Guid excluded)
            query = query.Where(e => e.EmployeeId != excluded);
        return query;
    }

    public void Update(MonitoringException exception) => _db.Exceptions.Update(exception);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
