using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

public record BillingAuditLogDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid? TenantId,
    [property: JsonPropertyName("invoice_id")] Guid? InvoiceId,
    [property: JsonPropertyName("actor_admin_user_id")] Guid? ActorAdminUserId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("metadata_json")] string? MetadataJson,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public record InvoiceDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_subscription_id")] Guid? TenantSubscriptionId,
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("external_invoice_id")] string? ExternalInvoiceId,
    [property: JsonPropertyName("gateway_provider")] string? GatewayProvider,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("subtotal_amount")] decimal SubtotalAmount,
    [property: JsonPropertyName("tax_amount")] decimal TaxAmount,
    [property: JsonPropertyName("discount_amount")] decimal DiscountAmount,
    [property: JsonPropertyName("total_amount")] decimal TotalAmount,
    [property: JsonPropertyName("period_start")] DateOnly? PeriodStart,
    [property: JsonPropertyName("period_end")] DateOnly? PeriodEnd,
    [property: JsonPropertyName("issued_at")] DateTimeOffset? IssuedAt,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("paid_at")] DateTimeOffset? PaidAt,
    [property: JsonPropertyName("voided_at")] DateTimeOffset? VoidedAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("audit_logs")] IReadOnlyList<BillingAuditLogDto> AuditLogs);
