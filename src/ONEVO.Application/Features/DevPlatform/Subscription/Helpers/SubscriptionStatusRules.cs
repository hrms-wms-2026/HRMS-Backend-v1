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

    public static bool IsActiveStatus(string status) =>
        ActiveStatuses.Contains(status, StringComparer.Ordinal);

    public static bool IsPastDue(string status) =>
        string.Equals(status, "past_due", StringComparison.Ordinal);

    public static bool IsInTrial(string status, DateTimeOffset? trialEndsAt, DateTimeOffset now) =>
        string.Equals(status, "trialing", StringComparison.Ordinal)
        && (!trialEndsAt.HasValue || trialEndsAt.Value > now);

    public static bool IsInGracePeriod(string status, DateTimeOffset? accessEndsAt, DateTimeOffset now) =>
        IsPastDue(status) && accessEndsAt.HasValue && accessEndsAt.Value > now;

    public static bool HasActiveAccess(string status, DateTimeOffset? accessEndsAt, DateTimeOffset now)
    {
        if (accessEndsAt.HasValue && accessEndsAt.Value <= now)
            return false;

        if (IsActiveStatus(status))
            return true;

        return status is "past_due" or "cancelled" && accessEndsAt > now;
    }

    public static int? DaysUntilRenewal(DateOnly currentPeriodEnd, DateTimeOffset now)
    {
        var renewalAt = currentPeriodEnd.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var days = (int)Math.Ceiling((renewalAt - now.UtcDateTime).TotalDays);
        return days < 0 ? 0 : days;
    }

    public static int? DaysUntilAccessEnds(DateTimeOffset? accessEndsAt, DateTimeOffset now)
    {
        if (!accessEndsAt.HasValue)
            return null;

        var days = (int)Math.Ceiling((accessEndsAt.Value - now).TotalDays);
        return days < 0 ? 0 : days;
    }
}
