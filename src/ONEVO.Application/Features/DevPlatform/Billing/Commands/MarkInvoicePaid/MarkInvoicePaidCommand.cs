using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.MarkInvoicePaid;

public sealed record MarkInvoicePaidCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>, IPlatformAuditedCommand<Result<InvoiceDetailDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceDetailDto> response) => new()
    {
        Action = "invoice.marked_paid",
        TargetType = "invoice",
        TargetId = InvoiceId.ToString(),
        TargetTenantId = response.Value?.TenantId,
        After = new { status = response.Value?.Status, paidAt = response.Value?.PaidAt },
    };
}
