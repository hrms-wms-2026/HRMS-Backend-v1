using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

public interface ILegalEntityRepository
{
    Task AddAsync(LegalEntity legalEntity, CancellationToken ct = default);
    Task<LegalEntity?> GetPrimaryByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
}
