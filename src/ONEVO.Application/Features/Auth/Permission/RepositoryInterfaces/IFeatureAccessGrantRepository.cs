using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IFeatureAccessGrantRepository
{
    Task<IReadOnlyList<FeatureAccessGrant>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default);
}
