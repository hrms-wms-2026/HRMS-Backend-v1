using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record SubscriptionInfoRequest(
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("billing_cycle")] string BillingCycle,
    [property: JsonPropertyName("commercial_model")] string CommercialModel,
    [property: JsonPropertyName("trial_period_days")] int? TrialPeriodDays = null,
    [property: JsonPropertyName("unpaid_grace_period_days")] int? UnpaidGracePeriodDays = null,
    [property: JsonPropertyName("included_seats")] int? IncludedSeats = null,
    [property: JsonPropertyName("overage_allowed")] bool? OverageAllowed = null,
    [property: JsonPropertyName("is_test")] bool IsTest = false);

public sealed record CreateTenantRequest(
    [property: JsonPropertyName("company_name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("industry_profile")] string IndustryProfile,
    [property: JsonPropertyName("company_size_range")] string CompanySizeRange,
    [property: JsonPropertyName("legal_entity_name")] string LegalEntityName,
    [property: JsonPropertyName("registration_number")] string? RegistrationNumber,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("subscription")] SubscriptionInfoRequest Subscription,
    [property: JsonPropertyName("owner_invite")] TenantOwnerInviteRequest? OwnerInvite = null);
