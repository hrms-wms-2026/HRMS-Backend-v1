using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".LegalEntity" segment would
// collide with the LegalEntity entity type and force using-aliases everywhere (same
// convention as IPositionRepository).
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public interface ILegalEntityRepository
{
    Task<IReadOnlyList<LegalEntity>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

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
