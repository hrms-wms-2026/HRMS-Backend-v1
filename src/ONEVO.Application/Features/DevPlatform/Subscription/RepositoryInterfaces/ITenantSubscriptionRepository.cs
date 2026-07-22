using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

public interface ITenantSubscriptionRepository
{
    Task AddAsync(TenantSubscription subscription, CancellationToken ct = default);
    Task<TenantSubscription?> GetByGatewaySubscriptionRefAsync(string gatewayRef, CancellationToken ct = default);
    Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the tenant's most recent subscription whose status grants live
    /// entitlement (see SubscriptionStatusRules.ActiveStatuses), or null.
    /// </summary>
    Task<TenantSubscription?> GetLatestActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
}
