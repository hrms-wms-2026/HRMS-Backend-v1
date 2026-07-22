namespace ONEVO.Application.Features.DevPlatform.Subscription.Helpers;

/// <summary>
/// Single source of truth for which tenant_subscriptions.status values grant
/// live entitlement (module access, storage allowance). Mirrors the status set
/// used by module entitlement resolution.
/// </summary>
public static class SubscriptionStatusRules
{
    public static readonly string[] ActiveStatuses =
    [
        "active",
        "trialing",
        "maintenance_included",
        "subscription_included"
    ];
}
