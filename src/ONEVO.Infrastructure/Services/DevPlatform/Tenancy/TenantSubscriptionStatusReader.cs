using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>
/// Reads the tenant_subscriptions row written by CreateTenantCommandHandler at
/// tenant creation. A tenant is always created with exactly one subscription
/// snapshot, so this section is complete as soon as that row exists.
/// </summary>
public sealed class TenantSubscriptionStatusReader : ITenantSubscriptionStatusReader
{
    private readonly ITenantSubscriptionRepository _subscriptions;

    public TenantSubscriptionStatusReader(ITenantSubscriptionRepository subscriptions) =>
        _subscriptions = subscriptions;

    public async Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
        {
            return NotConfiguredYetReaders.Build(
                section: "subscription",
                code: "subscription_not_configured",
                message: "subscription/commercial terms have not been configured for this tenant yet.");
        }

        return new ProvisioningSectionStatus(
            Complete: true,
            Summary: new Dictionary<string, object?>
            {
                ["plan_id"] = subscription.PlanId,
                ["status"] = subscription.Status,
                ["billing_cycle"] = subscription.BillingCycle,
                ["commercial_model"] = subscription.CommercialModel
            },
            MissingFields: Array.Empty<string>(),
            BlockingErrors: Array.Empty<ProvisioningIssue>(),
            Warnings: Array.Empty<ProvisioningIssue>());
    }
}
