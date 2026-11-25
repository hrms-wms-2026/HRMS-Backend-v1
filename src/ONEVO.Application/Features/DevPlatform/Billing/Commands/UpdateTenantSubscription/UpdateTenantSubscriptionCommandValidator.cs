using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateTenantSubscription;

public sealed class UpdateTenantSubscriptionCommandValidator : AbstractValidator<UpdateTenantSubscriptionCommand>
{
    public const int MaxSeats = 100_000;

    public UpdateTenantSubscriptionCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        When(x => x.IncludedSeats is not null, () =>
            RuleFor(x => x.IncludedSeats!.Value)
                .InclusiveBetween(0, MaxSeats)
                .WithName("included_seats"));
        RuleFor(x => x)
            .Must(x => x.IncludedSeats is not null || x.OverageAllowed is not null || x.IsTest is not null)
            .WithMessage("Send at least one of included_seats, overage_allowed or is_test.");
    }
}
