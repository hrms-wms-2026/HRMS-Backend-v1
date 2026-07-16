using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;

public sealed class ProcessStripeEventCommandHandler : IRequestHandler<ProcessStripeEventCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ProcessStripeEventCommandHandler(
        ITenantSubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _subscriptions = subscriptions;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ProcessStripeEventCommand request, CancellationToken ct)
    {
        return request.EventType switch
        {
            "checkout.session.completed" => await HandleCheckoutCompleted(request, ct),
            "invoice.payment_succeeded"  => await HandleInvoicePaymentSucceeded(request, ct),
            "invoice.payment_failed"     => await HandleInvoicePaymentFailed(request, ct),
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

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
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
        // Let them keep access until end of the paid period
        if (request.PeriodEnd.HasValue)
            sub.AccessEndsAt = request.PeriodEnd.Value;
        sub.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string MapStripeStatus(string stripeStatus) => stripeStatus switch
    {
        "active"              => "active",
        "trialing"            => "trialing",
        "past_due"            => "past_due",
        "canceled"            => "cancelled",
        "incomplete"          => "past_due",
        "incomplete_expired"  => "cancelled",
        "unpaid"              => "past_due",
        _                     => stripeStatus
    };
}
