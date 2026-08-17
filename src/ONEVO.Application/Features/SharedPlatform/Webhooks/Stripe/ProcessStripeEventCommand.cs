using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;

public record ProcessStripeEventCommand(
    string EventType,
    string? StripeSubscriptionId,
    string? StripeCustomerId,
    string? SubscriptionStatus,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    Guid? TenantIdFromMetadata,
    string? ExternalInvoiceId = null,
    string? InvoiceNumber = null,
    string? GatewayProvider = null,
    string? Currency = null,
    decimal? SubtotalAmount = null,
    decimal? TaxAmount = null,
    decimal? DiscountAmount = null,
    decimal? TotalAmount = null,
    DateTimeOffset? IssuedAt = null,
    DateTimeOffset? DueAt = null,
    DateTimeOffset? PaidAt = null) : IRequest<Result>;
