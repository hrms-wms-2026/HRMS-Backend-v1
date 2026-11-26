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
    string Status) : IRequest<Result<InvoiceDetailDto>>, IAuditedCommand<Result<InvoiceDetailDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceDetailDto>? response)
        => AuditEntry.For("invoice.created", "invoice", response?.Value?.Id, TenantId, new { InvoiceNumber = response?.Value?.InvoiceNumber, TenantSubscriptionId, Currency, SubtotalAmount, TaxAmount, DiscountAmount, PeriodStart, PeriodEnd, IssuedAt, DueAt, Status });
}
