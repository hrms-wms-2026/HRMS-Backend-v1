using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;

public class EfCheckInRepository : ICheckInRepository
{
    private readonly ApplicationDbContext _db;

    public EfCheckInRepository(ApplicationDbContext db) => _db = db;

    public async Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct)
        => await _db.EmployeeCheckIns.AddAsync(checkIn, ct);

    public async Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeCheckIns
            .FirstOrDefaultAsync(c => c.Id == checkInId && c.TenantId == tenantId, ct);

    public async Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct)
        => await _db.MonitoringFaceScans.AddAsync(faceScan, ct);

    public async Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct)
    {
        await _db.MonitoringFaceScans
            .Where(f => f.Id == faceScanId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, status), ct);
    }
}
