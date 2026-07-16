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
    Guid? TenantIdFromMetadata
) : IRequest<Result>;
