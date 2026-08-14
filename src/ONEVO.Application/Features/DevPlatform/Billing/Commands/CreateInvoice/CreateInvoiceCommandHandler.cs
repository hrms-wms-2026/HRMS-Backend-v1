using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateInvoice;

public sealed class CreateInvoiceCommandHandler
    : IRequestHandler<CreateInvoiceCommand, Result<InvoiceDetailDto>>
{
    private const int MaxInvoiceNumberAttempts = 5;

    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateInvoiceCommandHandler(
        ISubscriptionInvoiceRepository invoiceRepo,
        IBillingAuditLogRepository auditLogRepo,
        ITenantRepository tenantRepo,
        ICurrentPlatformUserContext platformUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoiceRepo = invoiceRepo;
        _auditLogRepo = auditLogRepo;
        _tenantRepo = tenantRepo;
        _platformUser = platformUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<InvoiceDetailDto>> Handle(CreateInvoiceCommand request, CancellationToken ct)
    {
        if (!InvoiceStatusRules.IsValid(request.Status))
            return Result<InvoiceDetailDto>.Failure(
                $"status '{request.Status}' is not a valid invoice status.");

        if (request.SubtotalAmount < 0 || request.TaxAmount < 0 || request.DiscountAmount < 0)
            return Result<InvoiceDetailDto>.Failure("Invoice amounts cannot be negative.");

        var totalAmount = request.SubtotalAmount + request.TaxAmount - request.DiscountAmount;
        if (totalAmount < 0)
            return Result<InvoiceDetailDto>.Failure(
                "Total amount cannot be negative after applying discount.");

        var tenant = await _tenantRepo.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<InvoiceDetailDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var now = _clock.UtcNow;
        var issuedAt = request.IssuedAt;
        if (request.Status == "open" && issuedAt is null)
            issuedAt = now;

        DateTimeOffset? paidAt = request.Status == "paid" ? now : null;
        DateTimeOffset? voidedAt = request.Status == "void" ? now : null;

        var invoiceNumber = await GenerateUniqueInvoiceNumberAsync(now, ct);
        if (invoiceNumber is null)
            return Result<InvoiceDetailDto>.Conflict("Unable to generate a unique invoice number.");

        var invoice = new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            TenantSubscriptionId = request.TenantSubscriptionId,
            InvoiceNumber = invoiceNumber,
            Status = request.Status,
            Currency = request.Currency.ToUpperInvariant(),
            SubtotalAmount = request.SubtotalAmount,
            TaxAmount = request.TaxAmount,
            DiscountAmount = request.DiscountAmount,
            TotalAmount = totalAmount,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            IssuedAt = issuedAt,
            DueAt = request.DueAt,
            PaidAt = paidAt,
            VoidedAt = voidedAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        var auditLog = new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            ActorAdminUserId = _platformUser.UserId,
            Action = "invoice.created",
            Message = $"Invoice {invoice.InvoiceNumber} created with status '{invoice.Status}'.",
            CreatedAt = now
        };

        await _invoiceRepo.AddAsync(invoice, ct);
        await _auditLogRepo.AddAsync(auditLog, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<InvoiceDetailDto>.Success(
            InvoiceMapper.ToDetailDto(invoice, [auditLog]));
    }

    private async Task<string?> GenerateUniqueInvoiceNumberAsync(DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxInvoiceNumberAttempts; attempt++)
        {
            var candidate = InvoiceNumberGenerator.Generate(now);
            var existing = await _invoiceRepo.GetByInvoiceNumberAsync(candidate, ct);
            if (existing is null)
                return candidate;
        }

        return null;
    }
}
