using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;

public class EfLegalEntityRepository : ILegalEntityRepository
{
    private readonly ApplicationDbContext _db;

    public EfLegalEntityRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(LegalEntity legalEntity, CancellationToken ct = default) =>
        await _db.LegalEntities.AddAsync(legalEntity, ct);

    public Task<LegalEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.LegalEntities.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == id, ct);

    public Task<LegalEntity?> GetPrimaryByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.LegalEntities.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsPrimary, ct);
}
