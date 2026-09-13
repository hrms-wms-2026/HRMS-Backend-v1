using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public class EfWorkModeRepository : IWorkModeRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkModeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TimeAttendanceWorkMode>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default)
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
