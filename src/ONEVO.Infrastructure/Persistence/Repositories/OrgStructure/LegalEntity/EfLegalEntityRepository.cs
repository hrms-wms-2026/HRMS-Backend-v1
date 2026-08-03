using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".LegalEntity" segment would
// collide with the LegalEntity entity type and force using-aliases everywhere (same
// convention as EfPositionRepository).
namespace ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

public class EfLegalEntityRepository : ILegalEntityRepository
{
    private readonly ApplicationDbContext _db;

    public EfLegalEntityRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LegalEntity>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId)
            .OrderBy(entity => entity.Name);

        var results = await query.ToListAsync(ct);
        return results;
    }

    public async Task<LegalEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == id);

        var result = await query.FirstOrDefaultAsync(ct);
        return result;
    }

    public async Task<LegalEntity?> GetPrimaryByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var result = await _db.LegalEntities.FirstOrDefaultAsync(
            entity => entity.TenantId == tenantId && entity.IsPrimary, ct);
        return result;
    }

    public async Task AddAsync(LegalEntity legalEntity, CancellationToken ct = default)
    {
        await _db.LegalEntities.AddAsync(legalEntity, ct);
    }

    public void Update(LegalEntity legalEntity)
    {
        _db.LegalEntities.Update(legalEntity);
    }

    public async Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.IsActive);

        var count = await query.CountAsync(ct);
        return count;
    }

    public async Task<bool> NameExistsForTenantAsync(
        Guid tenantId, string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Name == name);

        if (excludeId is not null)
        {
            query = query.Where(entity => entity.Id != excludeId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> CompanyCodeExistsForTenantAsync(
        Guid tenantId, string companyCode, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.CompanyCode == companyCode);

        if (excludeId is not null)
        {
            query = query.Where(entity => entity.Id != excludeId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> RegistrationNumberExistsForTenantAsync(
        Guid tenantId, string registrationNumber, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.RegistrationNumber == registrationNumber);

        if (excludeId is not null)
        {
            query = query.Where(entity => entity.Id != excludeId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> ParentExistsForTenantAsync(
        Guid tenantId, Guid parentLegalEntityId, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == parentLegalEntityId);

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> HasChildrenAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.ParentLegalEntityId == legalEntityId);

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var affectedRows = await _db.SaveChangesAsync(ct);
        return affectedRows;
    }
}
