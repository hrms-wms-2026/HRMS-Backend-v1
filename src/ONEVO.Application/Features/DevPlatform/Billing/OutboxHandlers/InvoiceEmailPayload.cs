namespace ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;

public sealed record InvoiceEmailPayload(
    Guid InvoiceId,
    Guid TenantId,
    string TenantName,
    string RecipientEmail,
    string InvoiceNumber,
    string Status,
    string Currency,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? PeriodStart,
    string? PeriodEnd,
    string? DueAt,
    string? PaidAt,
    bool IsReceipt);
