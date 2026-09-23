using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfAuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfAuditLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(AuditLog auditLog, CancellationToken ct = default)
    {
        var addTask = _db.AuditLogs.AddAsync(auditLog, ct).AsTask();
        return addTask;
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> ListByTenantIdAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.AuditLogs
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
