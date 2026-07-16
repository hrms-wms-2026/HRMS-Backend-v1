using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface ITenantAuthPolicyRepository
{
    Task<TenantAuthPolicy?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantAuthPolicy policy, CancellationToken ct = default);
}
