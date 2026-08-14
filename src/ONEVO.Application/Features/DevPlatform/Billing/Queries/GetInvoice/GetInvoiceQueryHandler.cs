using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetInvoice;

public sealed class GetInvoiceQueryHandler
    : IRequestHandler<GetInvoiceQuery, Result<InvoiceDetailDto>>
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;

    public GetInvoiceQueryHandler(
        ISubscriptionInvoiceRepository invoiceRepo,
        IBillingAuditLogRepository auditLogRepo)
    {
        _invoiceRepo = invoiceRepo;
        _auditLogRepo = auditLogRepo;
    }

    public async Task<Result<InvoiceDetailDto>> Handle(GetInvoiceQuery request, CancellationToken ct)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(request.InvoiceId, ct);
        if (invoice is null)
            return Result<InvoiceDetailDto>.NotFound($"Invoice '{request.InvoiceId}' not found.");

        var auditLogs = await _auditLogRepo.ListByInvoiceAsync(request.InvoiceId, ct);
        var orderedLogs = auditLogs.OrderByDescending(l => l.CreatedAt).ToList();

        return Result<InvoiceDetailDto>.Success(
            InvoiceMapper.ToDetailDto(invoice, orderedLogs));
    }
}
