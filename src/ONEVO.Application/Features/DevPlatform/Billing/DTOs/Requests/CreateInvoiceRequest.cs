using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Requests;

public record CreateInvoiceRequest(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_subscription_id")] Guid? TenantSubscriptionId,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("subtotal_amount")] decimal SubtotalAmount,
    [property: JsonPropertyName("tax_amount")] decimal TaxAmount,
    [property: JsonPropertyName("discount_amount")] decimal DiscountAmount,
    [property: JsonPropertyName("period_start")] DateOnly? PeriodStart,
    [property: JsonPropertyName("period_end")] DateOnly? PeriodEnd,
    [property: JsonPropertyName("issued_at")] DateTimeOffset? IssuedAt,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("status")] string Status = "draft");
