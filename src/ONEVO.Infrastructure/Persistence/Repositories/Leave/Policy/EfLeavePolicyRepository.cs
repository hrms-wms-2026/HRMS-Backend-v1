using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;

public class EfLeavePolicyRepository : ILeavePolicyRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeavePolicyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await _db.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && legalEntityIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }
}
