using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;

public sealed record ResendInvoiceEmailCommand(Guid InvoiceId)
    : IRequest<Result<InvoiceEmailResendResponseDto>>, IAuditedCommand<Result<InvoiceEmailResendResponseDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceEmailResendResponseDto>? response)
        => AuditEntry.For("invoice.email_resent", "invoice", InvoiceId, null, new { RecipientEmail = response?.Value?.RecipientEmail, DeliveryStatus = response?.Value?.DeliveryStatus });
}
