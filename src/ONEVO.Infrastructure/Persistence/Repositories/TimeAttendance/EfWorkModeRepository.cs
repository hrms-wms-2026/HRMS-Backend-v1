using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public class EfWorkModeRepository : IWorkModeRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantCache _cache;

    public EfWorkModeRepository(ApplicationDbContext db, ITenantCache? cache = null)
    {
        _db = db;
        _cache = cache ?? PassThroughTenantCache.Instance;
    }

    /// <summary>Cached per tenant and legal entity (task 22); every caller gets its own copies.</summary>
    public async Task<IReadOnlyList<TimeAttendanceWorkMode>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default)
    {
        var modes = await _cache.GetOrCreateAsync(
            tenantId, ReferenceDataCacheKeys.WorkModes, $"{legalEntityId:N}:{(includeInactive ? "all" : "active")}",
            token => LoadByLegalEntityAsync(tenantId, legalEntityId, includeInactive, token), ct);
        return ReferenceDataCopy.All(modes);
    }

    private async Task<IReadOnlyList<TimeAttendanceWorkMode>> LoadByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct)
    {
        var query = _db.TimeAttendanceWorkModes
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.LegalEntityId == legalEntityId);

        if (!includeInactive)
            query = query.Where(w => w.IsActive);

        return await query
            .OrderBy(w => w.DisplayOrder)
            .ThenBy(w => w.Name)
            .ToListAsync(ct);
    }

    public Task<TimeAttendanceWorkMode?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => _db.TimeAttendanceWorkModes.AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id, ct);

    public Task<TimeAttendanceWorkMode?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => _db.TimeAttendanceWorkModes.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id, ct);

    public Task<int> CountActiveAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => _db.TimeAttendanceWorkModes.AsNoTracking()
            .CountAsync(w => w.TenantId == tenantId && w.LegalEntityId == legalEntityId && w.IsActive, ct);

    public Task<bool> NameExistsAsync(
        Guid tenantId, Guid legalEntityId, string name, Guid? excludingId, CancellationToken ct = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return _db.TimeAttendanceWorkModes.AsNoTracking().AnyAsync(w =>
            w.TenantId == tenantId
            && w.LegalEntityId == legalEntityId
            && w.Name.ToLower() == normalized
            && (excludingId == null || w.Id != excludingId.Value), ct);
    }

    public async Task AddAsync(TimeAttendanceWorkMode workMode, CancellationToken ct = default)
    {
        await _db.TimeAttendanceWorkModes.AddAsync(workMode, ct);
    }

    public void Update(TimeAttendanceWorkMode workMode)
    {
        _db.TimeAttendanceWorkModes.Update(workMode);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _db.SaveChangesAsync(ct);
    }
}
