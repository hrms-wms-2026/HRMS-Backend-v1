using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.SharedPlatform.Webhooks.Stripe;

/// <summary>
/// Task 19. The webhook is signature-checked, but its values still land in tenant_subscriptions and
/// subscription_invoices, so they are bounded by those columns: gateway refs nvarchar(200), invoice_number
/// nvarchar(50), external_invoice_id nvarchar(100), gateway_provider nvarchar(50), currency nvarchar(3), amounts
/// decimal(12,2). Amounts may be negative (Stripe credits). A billing period is at most three years (the longest
/// Stripe interval) plus a week of slack. A rejected event returns 400, so Stripe retries it and it shows in the
/// Stripe dashboard.
/// </summary>
public sealed class ProcessStripeEventCommandValidator : AbstractValidator<ProcessStripeEventCommand>
{
    public const int EventTypeMaxLength = 100;
    public const int GatewayRefMaxLength = 200;
    public const int SubscriptionStatusMaxLength = 50;
    public const int ExternalInvoiceIdMaxLength = 100;
    public const int InvoiceNumberMaxLength = 50;
    public const int GatewayProviderMaxLength = 50;
    /// <summary>The largest value decimal(12,2) holds.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(3 * 366 + 7);

    private const string AmountMessage = "'{PropertyName}' must have at most two decimal places and fit decimal(12,2).";

    public ProcessStripeEventCommandValidator()
    {
        RuleFor(x => x.EventType).ValidName(EventTypeMaxLength);
        RuleFor(x => x.StripeSubscriptionId).ValidName(GatewayRefMaxLength, required: false);
        RuleFor(x => x.StripeCustomerId).ValidName(GatewayRefMaxLength, required: false);
        RuleFor(x => x.SubscriptionStatus).ValidName(SubscriptionStatusMaxLength, required: false);
        RuleFor(x => x.ExternalInvoiceId).ValidName(ExternalInvoiceIdMaxLength, required: false);
        RuleFor(x => x.InvoiceNumber).ValidName(InvoiceNumberMaxLength, required: false);
        RuleFor(x => x.GatewayProvider).ValidName(GatewayProviderMaxLength, required: false);
        RuleFor(x => x.Currency)
            .Matches("^[A-Za-z]{3}$").When(x => x.Currency is not null)
            .WithMessage("'{PropertyName}' must be a three-letter currency code.");

        RuleFor(x => x.SubtotalAmount).Must(BeStorableAmount).WithMessage(AmountMessage);
        RuleFor(x => x.TaxAmount).Must(BeStorableAmount).WithMessage(AmountMessage);
        RuleFor(x => x.DiscountAmount).Must(BeStorableAmount).WithMessage(AmountMessage);
        RuleFor(x => x.TotalAmount).Must(BeStorableAmount).WithMessage(AmountMessage);

        RuleFor(x => x).ValidDateRange(x => x.PeriodStart, x => x.PeriodEnd, MaxPeriod);
    }

    private static bool BeStorableAmount(decimal? amount) =>
        amount is not { } value || (Math.Abs(value) <= MaxAmount && decimal.Round(value, 2) == value);
}
