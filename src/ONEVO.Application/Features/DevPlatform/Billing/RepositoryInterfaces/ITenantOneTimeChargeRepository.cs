using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;

public interface ITenantOneTimeChargeRepository
{
    Task<IReadOnlyList<TenantOneTimeCharge>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantOneTimeCharge?> GetByIdAsync(Guid chargeId, CancellationToken ct = default);
    Task AddAsync(TenantOneTimeCharge charge, CancellationToken ct = default);
    Task<bool> HasActiveChargeAsync(Guid tenantId, string setupOptionKey, CancellationToken ct = default);
}
