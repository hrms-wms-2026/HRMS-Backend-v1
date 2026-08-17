using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class SubscriptionInvoice : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? TenantSubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? ExternalInvoiceId { get; set; }
    public string? GatewayProvider { get; set; }
    public string Status { get; set; } = "draft";
    public string Currency { get; set; } = "USD";
    public decimal SubtotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
