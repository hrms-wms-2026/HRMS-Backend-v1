using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.MarkInvoicePaid;

public sealed record MarkInvoicePaidCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>, IAuditedCommand<Result<InvoiceDetailDto>>
{
    public AuditEntry DescribeAudit(Result<InvoiceDetailDto>? response)
        => AuditEntry.For("invoice.marked_paid", "invoice", InvoiceId, response?.Value?.TenantId);
}
