using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;

public sealed record ResendInvoiceEmailCommand(Guid InvoiceId)
    : IRequest<Result<InvoiceEmailResendResponseDto>>, IPlatformAuditedCommand<Result<InvoiceEmailResendResponseDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceEmailResendResponseDto> response) => new()
    {
        Action = "invoice.email_resent",
        TargetType = "invoice",
        TargetId = InvoiceId.ToString(),
    };
}
