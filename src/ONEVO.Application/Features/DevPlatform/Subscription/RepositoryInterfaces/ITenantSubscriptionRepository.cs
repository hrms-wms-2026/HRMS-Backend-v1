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

    /// <summary>Of the given tenants, the ids whose subscription is a non-billed test subscription.</summary>
    Task<IReadOnlySet<Guid>> GetTestTenantIdsAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct = default);
}
