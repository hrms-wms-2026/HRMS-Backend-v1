using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Mappers;

public static class TenantSubscriptionMapper
{
    public static TenantSubscriptionDetailDto ToDetailDto(
        Tenant tenant,
        TenantSubscription subscription,
        SubscriptionPlan? plan,
        DateTimeOffset now)
    {
        var trialEndsAt = subscription.TrialEndDate;
        var accessEndsAt = subscription.AccessEndsAt;
        var graceEndsAt = SubscriptionStatusRules.IsPastDue(subscription.Status) ? accessEndsAt : null;

        return new TenantSubscriptionDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            subscription.Id,
            subscription.PlanId,
            plan?.Name,
            plan?.Code,
            subscription.Status,
            subscription.BillingCycle,
            subscription.BillingCurrency,
            ResolveAmount(subscription),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            trialEndsAt,
            graceEndsAt,
            accessEndsAt,
            subscription.UnpaidGracePeriodDays,
            subscription.GatewayProvider,
            subscription.GatewayCustomerRef,
            subscription.GatewaySubscriptionRef,
            subscription.MaintenanceStatus,
            subscription.MaintenanceBillingCycle,
            subscription.MaintenanceRenewalDate,
            subscription.MaintenanceAmount,
            subscription.CreatedAt,
            subscription.UpdatedAt,
            SubscriptionStatusRules.HasActiveAccess(subscription.Status, accessEndsAt, now),
            SubscriptionStatusRules.IsInTrial(subscription.Status, trialEndsAt, now),
            SubscriptionStatusRules.IsPastDue(subscription.Status),
            SubscriptionStatusRules.IsInGracePeriod(subscription.Status, accessEndsAt, now),
            SubscriptionStatusRules.DaysUntilRenewal(subscription.CurrentPeriodEnd, now),
            SubscriptionStatusRules.DaysUntilAccessEnds(accessEndsAt, now));
    }

    private static decimal ResolveAmount(TenantSubscription subscription) =>
        subscription.BillingCycle.Equals("annual", StringComparison.OrdinalIgnoreCase)
            ? subscription.OverrideAnnualPrice ?? subscription.CalculatedAnnualPrice
            : subscription.OverrideMonthlyPrice ?? subscription.CalculatedMonthlyPrice;
}
