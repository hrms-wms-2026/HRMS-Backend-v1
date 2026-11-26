using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Requests;

/// <summary>
/// Platform-admin edit of a tenant's seat allocation and test-tenant flag. Every field is
/// optional; only the fields sent are changed.
/// </summary>
public sealed record UpdateTenantSubscriptionRequest(
    [property: JsonPropertyName("included_seats")] int? IncludedSeats = null,
    [property: JsonPropertyName("overage_allowed")] bool? OverageAllowed = null,
    [property: JsonPropertyName("is_test")] bool? IsTest = null);
