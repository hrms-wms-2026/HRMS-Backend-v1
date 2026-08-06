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
    /// otherwise at most the single legal entity linked to the user's own active
    /// employees row. includeInactive is only honored on the management-access branch -
    /// a regular user's own company is only ever returned when it is active.
    /// </summary>
    Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(
        Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default);

    Task<LegalEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

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
