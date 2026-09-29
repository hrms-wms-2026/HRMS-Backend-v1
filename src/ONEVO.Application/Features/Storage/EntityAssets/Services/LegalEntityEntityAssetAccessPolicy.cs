using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

/// <summary>A company logo is shown in the topbar/side-navbar to every tenant user regardless of role - coverage-free by the same reasoning as EmployeeEntityAssetAccessPolicy.</summary>
public sealed class LegalEntityEntityAssetAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly ILegalEntityRepository _legalEntities;

    public LegalEntityEntityAssetAccessPolicy(ILegalEntityRepository legalEntities) => _legalEntities = legalEntities;

    public async Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
    {
        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, ownerId, ct);
        return entity is not null;
    }
}
