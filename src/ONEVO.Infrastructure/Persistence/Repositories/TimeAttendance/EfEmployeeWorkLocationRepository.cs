using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfEmployeeWorkLocationRepository(ApplicationDbContext db) : IEmployeeWorkLocationRepository
{
    public Task<EmployeeWorkLocation?> GetByEmployeeIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.EmployeeWorkLocations.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyDictionary<Guid, EmployeeWorkLocation>> ListByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, EmployeeWorkLocation>();

        var rows = await db.EmployeeWorkLocations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId))
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.EmployeeId);
    }

    public Task AddAsync(EmployeeWorkLocation location, CancellationToken ct = default)
        => db.EmployeeWorkLocations.AddAsync(location, ct).AsTask();

    public void Update(EmployeeWorkLocation location) => db.EmployeeWorkLocations.Update(location);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
