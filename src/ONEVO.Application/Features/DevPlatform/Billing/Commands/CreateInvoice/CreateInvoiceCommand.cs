using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateInvoice;

public sealed record CreateInvoiceCommand(
    Guid TenantId,
    Guid? TenantSubscriptionId,
    string Currency,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal DiscountAmount,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? DueAt,
    string Status) : IRequest<Result<InvoiceDetailDto>>, IPlatformAuditedCommand<Result<InvoiceDetailDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceDetailDto> response) => new()
    {
        Action = "invoice.created",
        TargetType = "invoice",
        TargetId = response.Value?.Id.ToString(),
        TargetTenantId = TenantId,
        After = new
        {
            invoiceNumber = response.Value?.InvoiceNumber,
            tenantSubscriptionId = TenantSubscriptionId,
            currency = Currency,
            subtotalAmount = SubtotalAmount,
            taxAmount = TaxAmount,
            discountAmount = DiscountAmount,
            totalAmount = response.Value?.TotalAmount,
            periodStart = PeriodStart,
            periodEnd = PeriodEnd,
            dueAt = DueAt,
            status = response.Value?.Status,
        },
    };
}
