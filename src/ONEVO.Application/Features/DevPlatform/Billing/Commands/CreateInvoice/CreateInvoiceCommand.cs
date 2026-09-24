using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;

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
    string Status) : IRequest<Result<InvoiceDetailDto>>;
