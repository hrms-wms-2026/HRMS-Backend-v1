using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

public interface ITenantSubscriptionRepository
{
    Task AddAsync(TenantSubscription subscription, CancellationToken ct = default);
    Task<TenantSubscription?> GetByGatewaySubscriptionRefAsync(string gatewayRef, CancellationToken ct = default);
    Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
}
