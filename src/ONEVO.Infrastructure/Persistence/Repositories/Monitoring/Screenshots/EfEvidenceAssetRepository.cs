using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots;

public class EfEvidenceAssetRepository : IEvidenceAssetRepository
{
    private readonly ApplicationDbContext _db;

    public EfEvidenceAssetRepository(ApplicationDbContext db) => _db = db;

    public void Add(MonitoringEvidenceAsset asset)
        => _db.MonitoringEvidenceAssets.Add(asset);

    public Task<MonitoringEvidenceAsset?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct)
        => _db.MonitoringEvidenceAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

    public async Task<(List<MonitoringEvidenceAsset> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        Guid? employeeId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var q = _db.MonitoringEvidenceAssets
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId.Value);

        if (from.HasValue)
        {
            var fromUtc = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(a => a.CapturedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(a => a.CapturedAt < toUtc);
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(a => a.CapturedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
