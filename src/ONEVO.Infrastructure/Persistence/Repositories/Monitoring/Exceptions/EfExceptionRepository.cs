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

    public async Task<bool> HasOpenOrEscalatedAsync(
        Guid tenantId, Guid employeeId, ExceptionType type, CancellationToken ct) =>
        await _db.Exceptions.AsNoTracking().AnyAsync(
            e => e.TenantId == tenantId && e.EmployeeId == employeeId && e.Type == type
                 && (e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.Escalated), ct);

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

    public async Task<IReadOnlyList<MonitoringException>> GetListAsync(
        Guid tenantId, ExceptionStatus? status, ExceptionType? type, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Exceptions.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (status.HasValue) query = query.Where(e => e.Status == status.Value);
        if (type.HasValue) query = query.Where(e => e.Type == type.Value);

        return await query
            .OrderByDescending(e => e.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetListTotalCountAsync(
        Guid tenantId, ExceptionStatus? status, ExceptionType? type, CancellationToken ct)
    {
        var query = _db.Exceptions.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (status.HasValue) query = query.Where(e => e.Status == status.Value);
        if (type.HasValue) query = query.Where(e => e.Type == type.Value);
        return await query.CountAsync(ct);
    }

    public void Update(MonitoringException exception) => _db.Exceptions.Update(exception);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
