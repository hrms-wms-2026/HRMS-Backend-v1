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

    public async Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(
        Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default)
    {
        if (hasManagementAccess)
        {
            var query = _db.LegalEntities
                .AsNoTracking()
                .Where(entity => entity.TenantId == tenantId);

            if (!includeInactive)
                query = query.Where(entity => entity.IsActive);

            return await query.OrderBy(entity => entity.Name).ToListAsync(ct);
        }

        // Regular user: at most the one legal entity their own active employee row
        // points at. includeInactive is deliberately ignored here - a non-admin user
        // must never discover an archived company by flipping a query flag, and their
        // own company is only surfaced while it is active.
        var employeeLegalEntityId = await ResolveOwnActiveLegalEntityIdAsync(tenantId, userId, ct);
        if (employeeLegalEntityId is null)
            return [];

        var own = await _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == employeeLegalEntityId.Value && entity.IsActive)
            .FirstOrDefaultAsync(ct);

        return own is null ? [] : [own];
    }

    public async Task<LegalEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var query = _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == id);

        var result = await query.FirstOrDefaultAsync(ct);
        return result;
    }

    public async Task<LegalEntity?> GetAccessibleByIdAsync(
        Guid tenantId, Guid id, Guid userId, bool hasManagementAccess, CancellationToken ct = default)
    {
        if (hasManagementAccess)
        {
            return await _db.LegalEntities
                .AsNoTracking()
                .Where(entity => entity.TenantId == tenantId && entity.Id == id)
                .FirstOrDefaultAsync(ct);
        }

        // Regular user: only ever their own active employee's legal entity, and only
        // while that entity is itself active - the same rule ListAccessibleAsync's
        // non-management branch applies, kept in sync via the shared helper below.
        var employeeLegalEntityId = await ResolveOwnActiveLegalEntityIdAsync(tenantId, userId, ct);
        if (employeeLegalEntityId != id)
            return null;

        return await _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == id && entity.IsActive)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Guid?> ResolveOwnActiveLegalEntityIdAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        return await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId && employee.UserId == userId && status.Code == "active"
            select (Guid?)employee.LegalEntityId)
            .FirstOrDefaultAsync(ct);
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
