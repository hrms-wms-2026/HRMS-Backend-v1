using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Audit;

public sealed class EfPlatformAuditLogRepository : IPlatformAuditLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformAuditLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // Caller must call IUnitOfWork.SaveChangesAsync.
    public Task AddAsync(PlatformAuditLog entry, CancellationToken ct = default) =>
        _db.PlatformAuditLogs.AddAsync(entry, ct).AsTask();

    public async Task<(IReadOnlyList<PlatformAuditLog> Items, int Total)> ListAsync(
        PlatformAuditLogFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.PlatformAuditLogs.AsNoTracking();

        if (filter.ActorPlatformUserId is { } actor)
            query = query.Where(e => e.ActorPlatformUserId == actor);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(e => e.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
            query = query.Where(e => e.TargetType == filter.TargetType);
        if (!string.IsNullOrWhiteSpace(filter.TargetId))
            query = query.Where(e => e.TargetId == filter.TargetId);
        if (filter.TargetTenantId is { } tenantId)
            query = query.Where(e => e.TargetTenantId == tenantId);
        if (filter.From is { } from)
            query = query.Where(e => e.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(e => e.CreatedAt < to);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
