using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("company_name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("industry_profile")] string IndustryProfile,
    [property: JsonPropertyName("company_size_range")] string CompanySizeRange,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("subscription_plan_id")] Guid? SubscriptionPlanId,
    [property: JsonPropertyName("settings_json")] string? SettingsJson,
    [property: JsonPropertyName("legal_entity_name")] string? LegalEntityName,
    [property: JsonPropertyName("registration_number")] string? RegistrationNumber,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);
