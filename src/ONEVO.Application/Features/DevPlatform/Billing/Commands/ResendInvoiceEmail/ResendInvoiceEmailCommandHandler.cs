using System.Globalization;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.ResendInvoiceEmail;

public sealed class ResendInvoiceEmailCommandHandler
    : IRequestHandler<ResendInvoiceEmailCommand, Result<InvoiceEmailResendResponseDto>>
{
    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IInvitationTokenRepository _invitations;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ResendInvoiceEmailCommandHandler(
        ISubscriptionInvoiceRepository invoiceRepo,
        IBillingAuditLogRepository auditLogRepo,
        ITenantRepository tenantRepo,
        ILegalEntityRepository legalEntities,
        IInvitationTokenRepository invitations,
        ICurrentPlatformUserContext platformUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoiceRepo = invoiceRepo;
        _auditLogRepo = auditLogRepo;
        _tenantRepo = tenantRepo;
        _legalEntities = legalEntities;
        _invitations = invitations;
        _platformUser = platformUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<InvoiceEmailResendResponseDto>> Handle(
        ResendInvoiceEmailCommand request,
        CancellationToken ct)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(request.InvoiceId, ct);
        if (invoice is null)
            return Result<InvoiceEmailResendResponseDto>.NotFound($"Invoice '{request.InvoiceId}' not found.");

        if (!InvoiceStatusRules.CanResendEmail(invoice.Status))
            return Result<InvoiceEmailResendResponseDto>.Failure(
                $"Cannot resend invoice email for invoice in status '{invoice.Status}'.",
                409);

        var tenant = await _tenantRepo.GetByIdAsync(invoice.TenantId, ct);
        if (tenant is null)
            return Result<InvoiceEmailResendResponseDto>.NotFound($"Tenant '{invoice.TenantId}' not found.");

        var recipientEmail = await InvoiceBillingEmailResolver.ResolveRecipientEmailAsync(
            invoice.TenantId,
            _legalEntities,
            _invitations,
            ct);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return Result<InvoiceEmailResendResponseDto>.Failure(
                "No billing contact email is configured for this tenant.",
                422);
        }

        var now = _clock.UtcNow;
        var isReceipt = invoice.Status == "paid";

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.InvoiceEmail,
            new InvoiceEmailPayload(
                invoice.Id,
                invoice.TenantId,
                tenant.Name,
                recipientEmail,
                invoice.InvoiceNumber,
                invoice.Status,
                invoice.Currency,
                invoice.SubtotalAmount,
                invoice.TaxAmount,
                invoice.DiscountAmount,
                invoice.TotalAmount,
                FormatDate(invoice.PeriodStart),
                FormatDate(invoice.PeriodEnd),
                FormatDateTime(invoice.DueAt),
                FormatDateTime(invoice.PaidAt),
                isReceipt),
            invoice.TenantId,
            ct);

        await _auditLogRepo.AddAsync(new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            ActorAdminUserId = _platformUser.UserId,
            Action = "invoice.email_resent",
            Message = $"Invoice {invoice.InvoiceNumber} email queued to {recipientEmail}.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                recipient_email = recipientEmail,
                delivery_status = "queued",
                is_receipt = isReceipt
            }),
            CreatedAt = now
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<InvoiceEmailResendResponseDto>.Success(
            new InvoiceEmailResendResponseDto(invoice.Id, recipientEmail, "queued"));
    }

    private static string? FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? FormatDateTime(DateTimeOffset? value) =>
        value?.ToString("u", CultureInfo.InvariantCulture);
}
