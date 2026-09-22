using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfExternalCalendarConnectionRepository : IExternalCalendarConnectionRepository
{
    private readonly ApplicationDbContext _db;

    public EfExternalCalendarConnectionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ExternalCalendarConnection connection, CancellationToken ct = default)
        => await _db.ExternalCalendarConnections.AddAsync(connection, ct);

    public async Task<ExternalCalendarConnection?> GetByTenantUserProviderAsync(Guid tenantId, Guid userId, string provider, CancellationToken ct = default)
        => await _db.ExternalCalendarConnections.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.UserId == userId && c.Provider == provider, ct);

    public async Task<ExternalCalendarConnection?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.ExternalCalendarConnections.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<ExternalCalendarConnection?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.ExternalCalendarConnections.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<IReadOnlyList<ExternalCalendarConnection>> GetForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await _db.ExternalCalendarConnections.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.UserId == userId)
            .OrderBy(c => c.Provider)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExternalCalendarConnection>> GetActiveAsync(CancellationToken ct = default)
        => await _db.ExternalCalendarConnections
            .Where(c => c.Status != ExternalCalendarConnectionStatuses.Failed && c.Status != ExternalCalendarConnectionStatuses.Revoked)
            .ToListAsync(ct);

    public void Update(ExternalCalendarConnection connection) => _db.ExternalCalendarConnections.Update(connection);
    public void Remove(ExternalCalendarConnection connection) => _db.ExternalCalendarConnections.Remove(connection);
}
