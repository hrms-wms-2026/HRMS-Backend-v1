using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".LegalEntity" segment would
// collide with the LegalEntity entity type and force using-aliases everywhere (same
// convention as IPositionRepository).
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public interface ILegalEntityRepository
{
    /// <summary>
    /// Returns the legal entities the given user may see: every (optionally including
    /// inactive) tenant legal entity when <paramref name="hasManagementAccess"/> is true,
    /// otherwise every legal entity linked to one of the user's own active employee
    /// rows. includeInactive is only honored on the management-access branch - a
    /// regular user's companies are only ever returned while active.
    /// </summary>
    Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(
        Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default);

    /// <summary>
    /// Returns the single legal entity identified by <paramref name="id"/> if the caller
    /// may access it: any tenant entity (active or not) when
    /// <paramref name="hasManagementAccess"/> is true, otherwise only the caller's own
    /// active employee's legal entity, and only when that entity is itself active and
    /// equal to <paramref name="id"/>. Null in every other case (unknown id, wrong
    /// tenant, or not the caller's own company).
    /// </summary>
    Task<LegalEntity?> GetAccessibleByIdAsync(
        Guid tenantId, Guid id, Guid userId, bool hasManagementAccess, CancellationToken ct = default);

    Task<LegalEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    // Cross-tenant background jobs (no acting user) need every active legal entity for one tenant.
    // ListAccessibleAsync always requires a userId even on its management-access branch, so it isn't
    // usable from a BackgroundService.
    Task<IReadOnlyList<LegalEntity>> ListActiveForTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<LegalEntity?> GetPrimaryByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(LegalEntity legalEntity, CancellationToken ct = default);

    void Update(LegalEntity legalEntity);

    Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<bool> NameExistsForTenantAsync(Guid tenantId, string name, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> CompanyCodeExistsForTenantAsync(Guid tenantId, string companyCode, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> RegistrationNumberExistsForTenantAsync(Guid tenantId, string registrationNumber, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> ParentExistsForTenantAsync(Guid tenantId, Guid parentLegalEntityId, CancellationToken ct = default);

    Task<bool> HasChildrenAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
