using MediatR;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;
using Stripe;
using Stripe.Checkout;

namespace ONEVO.Api.Controllers.Webhooks;

[ApiController]
[Route("api/payment-gateways/stripe/{gatewayKey}/webhook")]
public class StripeWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPaymentGatewayWebhookSecretResolver _webhookSecrets;

    public StripeWebhookController(
        IMediator mediator,
        IPaymentGatewayWebhookSecretResolver webhookSecrets)
    {
        _mediator = mediator;
        _webhookSecrets = webhookSecrets;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(
        [FromRoute] string gatewayKey,
        CancellationToken ct)
    {
        if (!PaymentGatewayKeyRules.IsValid(gatewayKey))
        {
            return BadRequest("Invalid payment gateway key.");
        }

        var webhookSecret = await _webhookSecrets.ResolveWebhookSecretAsync(
            "stripe",
            gatewayKey,
            ct);

        return await ProcessWebhookWithSecretAsync(webhookSecret, ct);
    }

    [HttpPost("/api/payment-gateways/stripe/{gatewayConfigId:guid}/webhook")]
    public async Task<IActionResult> HandleByConfigId(
        [FromRoute] Guid gatewayConfigId,
        CancellationToken ct)
    {
        var webhookSecret = await _webhookSecrets.ResolveByConfigIdAsync(
            "stripe",
            gatewayConfigId,
            ct);

        return await ProcessWebhookWithSecretAsync(webhookSecret, ct);
    }

    private async Task<IActionResult> ProcessWebhookWithSecretAsync(
        string? webhookSecret,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return BadRequest("Stripe webhook is not configured.");
        }

        string payload;
        using (var reader = new StreamReader(HttpContext.Request.Body, System.Text.Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync(ct);
        }

        var signature = HttpContext.Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
        catch (StripeException)
        {
            return BadRequest("Invalid Stripe signature.");
        }

        var command = ExtractCommand(stripeEvent);
        if (command is not null)
        {
            await _mediator.Send(command, ct);
        }

        return Ok();
    }

    private static ProcessStripeEventCommand? ExtractCommand(Event e) => e.Type switch
    {
        "checkout.session.completed"    => ExtractFromCheckoutSession(e),
        "invoice.payment_succeeded"     => ExtractFromInvoice(e),
        "invoice.payment_failed"        => ExtractFromInvoice(e),
        "customer.subscription.updated" => ExtractFromSubscription(e),
        "customer.subscription.deleted" => ExtractFromSubscription(e),
        _ => null
    };

    private static ProcessStripeEventCommand ExtractFromCheckoutSession(Event e)
    {
        var session = e.Data.Object as Session;

        Guid? tenantId = null;
        if (session?.Metadata?.TryGetValue("tenant_id", out var tid) == true && Guid.TryParse(tid, out var parsed))
        {
            tenantId = parsed;
        }

        return new ProcessStripeEventCommand(
            e.Type,
            session?.SubscriptionId,
            session?.CustomerId,
            null,
            null,
            null,
            tenantId);
    }

    private static ProcessStripeEventCommand ExtractFromInvoice(Event e)
    {
        var invoice = e.Data.Object as Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;

        var periodStart = invoice?.PeriodStart is DateTime ps && ps != default
            ? new DateTimeOffset(ps, TimeSpan.Zero) : (DateTimeOffset?)null;
        var periodEnd = invoice?.PeriodEnd is DateTime pe && pe != default
            ? new DateTimeOffset(pe, TimeSpan.Zero) : (DateTimeOffset?)null;

        return new ProcessStripeEventCommand(
            e.Type,
            subscriptionId,
            invoice?.CustomerId,
            null,
            periodStart,
            periodEnd,
            null);
    }

    private static ProcessStripeEventCommand ExtractFromSubscription(Event e)
    {
        var sub = e.Data.Object as Subscription;

        var firstItem = sub?.Items?.Data?.FirstOrDefault();
        var periodStart = firstItem?.CurrentPeriodStart is DateTime ps && ps != default
            ? new DateTimeOffset(ps, TimeSpan.Zero) : (DateTimeOffset?)null;
        var periodEnd = firstItem?.CurrentPeriodEnd is DateTime pe && pe != default
            ? new DateTimeOffset(pe, TimeSpan.Zero) : (DateTimeOffset?)null;

        return new ProcessStripeEventCommand(
            e.Type,
            sub?.Id,
            sub?.CustomerId,
            sub?.Status,
            periodStart,
            periodEnd,
            null);
    }
}
