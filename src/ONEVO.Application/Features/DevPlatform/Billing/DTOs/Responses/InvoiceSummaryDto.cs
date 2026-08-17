using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

public record InvoiceSummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_subscription_id")] Guid? TenantSubscriptionId,
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("total_amount")] decimal TotalAmount,
    [property: JsonPropertyName("issued_at")] DateTimeOffset? IssuedAt,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("paid_at")] DateTimeOffset? PaidAt,
    [property: JsonPropertyName("voided_at")] DateTimeOffset? VoidedAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public record InvoiceListResponseDto(
    [property: JsonPropertyName("items")] IReadOnlyList<InvoiceSummaryDto> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);
