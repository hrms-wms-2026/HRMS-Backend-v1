using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfModuleHourSummaryRepository : IModuleHourSummaryRepository
{
    private readonly ApplicationDbContext _db;

    public EfModuleHourSummaryRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, ModuleHourSummary>> ListByObjectiveIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return new Dictionary<Guid, ModuleHourSummary>();
        var ids = objectiveIds.ToList();
        return await _db.ModuleHourSummaries.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
    }
}
