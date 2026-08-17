using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;

public sealed class ProcessStripeEventCommandHandler : IRequestHandler<ProcessStripeEventCommand, Result>
{
    private const int MaxInvoiceNumberAttempts = 5;

    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly ISubscriptionInvoiceRepository _invoiceRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ProcessStripeEventCommandHandler(
        ITenantSubscriptionRepository subscriptions,
        ISubscriptionInvoiceRepository invoiceRepo,
        IBillingAuditLogRepository auditLogRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _subscriptions = subscriptions;
        _invoiceRepo = invoiceRepo;
        _auditLogRepo = auditLogRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ProcessStripeEventCommand request, CancellationToken ct)
    {
        return request.EventType switch
        {
            "checkout.session.completed" => await HandleCheckoutCompleted(request, ct),
            "invoice.payment_succeeded" => await HandleInvoicePaymentSucceeded(request, ct),
            "invoice.payment_failed" => await HandleInvoicePaymentFailed(request, ct),
            "customer.subscription.updated" => await HandleSubscriptionUpdated(request, ct),
            "customer.subscription.deleted" => await HandleSubscriptionDeleted(request, ct),
            _ => Result.Success()
        };
    }

    private async Task<Result> HandleCheckoutCompleted(ProcessStripeEventCommand request, CancellationToken ct)
    {
        if (request.TenantIdFromMetadata is null || request.StripeSubscriptionId is null)
            return Result.Success();

        var sub = await _subscriptions.GetByTenantIdAsync(request.TenantIdFromMetadata.Value, ct);
        if (sub is null)
            return Result.Success();

        sub.GatewayProvider = "stripe";
        sub.GatewaySubscriptionRef = request.StripeSubscriptionId;
        sub.GatewayCustomerRef = request.StripeCustomerId;
        sub.Status = "active";
        sub.UpdatedAt = _clock.UtcNow;

        if (request.PeriodStart.HasValue)
            sub.CurrentPeriodStart = DateOnly.FromDateTime(request.PeriodStart.Value.UtcDateTime);
        if (request.PeriodEnd.HasValue)
            sub.CurrentPeriodEnd = DateOnly.FromDateTime(request.PeriodEnd.Value.UtcDateTime);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> HandleInvoicePaymentSucceeded(ProcessStripeEventCommand request, CancellationToken ct)
    {
        if (request.StripeSubscriptionId is null)
            return Result.Success();

        var sub = await _subscriptions.GetByGatewaySubscriptionRefAsync(request.StripeSubscriptionId, ct);
        if (sub is null)
            return Result.Success();

        sub.Status = "active";
        sub.AccessEndsAt = null;
        sub.UpdatedAt = _clock.UtcNow;

        if (request.PeriodStart.HasValue)
            sub.CurrentPeriodStart = DateOnly.FromDateTime(request.PeriodStart.Value.UtcDateTime);
        if (request.PeriodEnd.HasValue)
            sub.CurrentPeriodEnd = DateOnly.FromDateTime(request.PeriodEnd.Value.UtcDateTime);

        await UpsertInvoiceFromStripeAsync(request, sub, "paid", ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> HandleInvoicePaymentFailed(ProcessStripeEventCommand request, CancellationToken ct)
    {
        if (request.StripeSubscriptionId is null)
            return Result.Success();

        var sub = await _subscriptions.GetByGatewaySubscriptionRefAsync(request.StripeSubscriptionId, ct);
        if (sub is null)
            return Result.Success();

        sub.Status = "past_due";
        sub.AccessEndsAt = _clock.UtcNow.AddDays(sub.UnpaidGracePeriodDays);
        sub.UpdatedAt = _clock.UtcNow;

        await UpsertInvoiceFromStripeAsync(request, sub, "open", ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task UpsertInvoiceFromStripeAsync(
        ProcessStripeEventCommand request,
        TenantSubscription subscription,
        string invoiceStatus,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalInvoiceId))
            return;

        var now = _clock.UtcNow;
        var existing = await _invoiceRepo.GetByExternalInvoiceIdAsync(request.ExternalInvoiceId, ct);
        var isCreate = existing is null;

        var invoice = existing ?? new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = subscription.TenantId,
            TenantSubscriptionId = subscription.Id,
            ExternalInvoiceId = request.ExternalInvoiceId,
            InvoiceNumber = await ResolveInvoiceNumberAsync(request.InvoiceNumber, now, ct),
            GatewayProvider = request.GatewayProvider ?? "stripe",
            CreatedAt = now
        };

        ApplyInvoiceFields(invoice, request, subscription, invoiceStatus, now);

        if (isCreate)
            await _invoiceRepo.AddAsync(invoice, ct);
        else
            await _invoiceRepo.UpdateAsync(invoice, ct);

        await _auditLogRepo.AddAsync(new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = subscription.TenantId,
            InvoiceId = invoice.Id,
            Action = "invoice.synced_from_stripe",
            Message = $"Stripe invoice {request.ExternalInvoiceId} synced from {request.EventType}.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                event_type = request.EventType,
                external_invoice_id = request.ExternalInvoiceId,
                invoice_status = invoiceStatus,
                operation = isCreate ? "create" : "update"
            }),
            CreatedAt = now
        }, ct);
    }

    private static void ApplyInvoiceFields(
        SubscriptionInvoice invoice,
        ProcessStripeEventCommand request,
        TenantSubscription subscription,
        string invoiceStatus,
        DateTimeOffset now)
    {
        invoice.TenantId = subscription.TenantId;
        invoice.TenantSubscriptionId = subscription.Id;
        invoice.GatewayProvider = request.GatewayProvider ?? invoice.GatewayProvider ?? "stripe";
        invoice.Status = invoiceStatus;
        invoice.Currency = (request.Currency ?? invoice.Currency ?? subscription.BillingCurrency).ToUpperInvariant();
        invoice.SubtotalAmount = request.SubtotalAmount ?? invoice.SubtotalAmount;
        invoice.TaxAmount = request.TaxAmount ?? invoice.TaxAmount;
        invoice.DiscountAmount = request.DiscountAmount ?? invoice.DiscountAmount;
        invoice.TotalAmount = request.TotalAmount ?? invoice.TotalAmount;

        if (request.PeriodStart.HasValue)
            invoice.PeriodStart = DateOnly.FromDateTime(request.PeriodStart.Value.UtcDateTime);
        if (request.PeriodEnd.HasValue)
            invoice.PeriodEnd = DateOnly.FromDateTime(request.PeriodEnd.Value.UtcDateTime);
        if (request.IssuedAt.HasValue)
            invoice.IssuedAt = request.IssuedAt;
        if (request.DueAt.HasValue)
            invoice.DueAt = request.DueAt;

        invoice.PaidAt = invoiceStatus == "paid"
            ? request.PaidAt ?? invoice.PaidAt ?? now
            : invoice.PaidAt;

        invoice.UpdatedAt = now;
    }

    private async Task<string> ResolveInvoiceNumberAsync(
        string? stripeInvoiceNumber,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(stripeInvoiceNumber))
            return stripeInvoiceNumber;

        for (var attempt = 0; attempt < MaxInvoiceNumberAttempts; attempt++)
        {
            var candidate = InvoiceNumberGenerator.Generate(now);
            var existing = await _invoiceRepo.GetByInvoiceNumberAsync(candidate, ct);
            if (existing is null)
                return candidate;
        }

        return InvoiceNumberGenerator.Generate(now);
    }

    private async Task<Result> HandleSubscriptionUpdated(ProcessStripeEventCommand request, CancellationToken ct)
    {
        if (request.StripeSubscriptionId is null)
            return Result.Success();

        var sub = await _subscriptions.GetByGatewaySubscriptionRefAsync(request.StripeSubscriptionId, ct);
        if (sub is null)
            return Result.Success();

        if (request.SubscriptionStatus is not null)
            sub.Status = MapStripeStatus(request.SubscriptionStatus);

        sub.UpdatedAt = _clock.UtcNow;

        if (request.PeriodStart.HasValue)
            sub.CurrentPeriodStart = DateOnly.FromDateTime(request.PeriodStart.Value.UtcDateTime);
        if (request.PeriodEnd.HasValue)
            sub.CurrentPeriodEnd = DateOnly.FromDateTime(request.PeriodEnd.Value.UtcDateTime);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> HandleSubscriptionDeleted(ProcessStripeEventCommand request, CancellationToken ct)
    {
        if (request.StripeSubscriptionId is null)
            return Result.Success();

        var sub = await _subscriptions.GetByGatewaySubscriptionRefAsync(request.StripeSubscriptionId, ct);
        if (sub is null)
            return Result.Success();

        sub.Status = "cancelled";
        if (request.PeriodEnd.HasValue)
            sub.AccessEndsAt = request.PeriodEnd.Value;
        sub.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string MapStripeStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => "active",
        "trialing" => "trialing",
        "past_due" => "past_due",
        "canceled" => "cancelled",
        "incomplete" => "past_due",
        "incomplete_expired" => "cancelled",
        "unpaid" => "past_due",
        _ => stripeStatus
    };
}
