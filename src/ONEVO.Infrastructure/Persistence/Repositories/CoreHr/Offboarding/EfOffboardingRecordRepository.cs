using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;

public sealed class EfOffboardingRecordRepository(ApplicationDbContext db) : IOffboardingRecordRepository
{
    public Task<OffboardingRecord?> GetOpenByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.OffboardingRecords.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId
            && (x.Status == OffboardingRecordStatuses.Initiated || x.Status == OffboardingRecordStatuses.InProgress), ct);

    public Task<OffboardingRecord?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.OffboardingRecords.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<OffboardingRecord?> GetLatestByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.OffboardingRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetLatestStatusesByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await db.OffboardingRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId))
            .GroupBy(x => x.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Status = g.OrderByDescending(x => x.CreatedAt).First().Status })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.EmployeeId, r => r.Status);
    }

    public Task AddAsync(OffboardingRecord record, CancellationToken ct = default)
        => db.OffboardingRecords.AddAsync(record, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
