using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Helpers;

/// <summary>
/// Values of tenant_subscriptions.commercial_model. A "test" tenant is an internal/testing
/// tenant: it is never billed, never trials out and never reacts to payment-gateway events,
/// and its seats are whatever a platform admin allocates.
/// </summary>
public static class CommercialModels
{
    public const string Subscription = "subscription";
    public const string Test = "test";

    public static bool IsTest(string? commercialModel) =>
        string.Equals(commercialModel, Test, StringComparison.OrdinalIgnoreCase);

    public static bool IsTest(TenantSubscription subscription) => IsTest(subscription.CommercialModel);

    /// <summary>Makes the subscription a non-billed, always-active test subscription.</summary>
    public static void ApplyTestMode(TenantSubscription subscription)
    {
        subscription.CommercialModel = Test;
        subscription.Status = "active";
        subscription.TrialStartDate = null;
        subscription.TrialEndDate = null;
        subscription.AccessEndsAt = null;
    }
}
