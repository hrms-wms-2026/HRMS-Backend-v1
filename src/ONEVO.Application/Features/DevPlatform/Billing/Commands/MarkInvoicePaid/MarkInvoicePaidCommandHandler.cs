using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.MarkInvoicePaid;

public sealed class MarkInvoicePaidCommandHandler
    : IRequestHandler<MarkInvoicePaidCommand, Result<InvoiceDetailDto>>
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public MarkInvoicePaidCommandHandler(
        ISubscriptionInvoiceRepository invoiceRepo,
        IBillingAuditLogRepository auditLogRepo,
        ICurrentPlatformUserContext platformUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoiceRepo = invoiceRepo;
        _auditLogRepo = auditLogRepo;
        _platformUser = platformUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<InvoiceDetailDto>> Handle(MarkInvoicePaidCommand request, CancellationToken ct)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(request.InvoiceId, ct);
        if (invoice is null)
            return Result<InvoiceDetailDto>.NotFound($"Invoice '{request.InvoiceId}' not found.");

        if (!InvoiceStatusRules.CanMarkPaid(invoice.Status))
            return Result<InvoiceDetailDto>.Failure(
                $"Cannot mark invoice in status '{invoice.Status}' as paid.",
                409);

        var now = _clock.UtcNow;
        invoice.Status = "paid";
        invoice.PaidAt = now;
        invoice.UpdatedAt = now;

        var auditLog = new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            ActorAdminUserId = _platformUser.UserId,
            Action = "invoice.marked_paid",
            Message = $"Invoice {invoice.InvoiceNumber} marked as paid.",
            CreatedAt = now
        };

        await _invoiceRepo.UpdateAsync(invoice, ct);
        await _auditLogRepo.AddAsync(auditLog, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var auditLogs = await _auditLogRepo.ListByInvoiceAsync(invoice.Id, ct);
        var orderedLogs = auditLogs.OrderByDescending(l => l.CreatedAt).ToList();

        return Result<InvoiceDetailDto>.Success(InvoiceMapper.ToDetailDto(invoice, orderedLogs));
    }
}
