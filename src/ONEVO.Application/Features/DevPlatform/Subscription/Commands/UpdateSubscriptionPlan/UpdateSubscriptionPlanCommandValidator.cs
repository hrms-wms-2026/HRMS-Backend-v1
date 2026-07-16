using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;

public class UpdateSubscriptionPlanCommandValidator : AbstractValidator<UpdateSubscriptionPlanCommand>
{
    public UpdateSubscriptionPlanCommandValidator()
    {
        When(x => x.Name is not null, () =>
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100));
        When(x => x.Tier is not null, () =>
            RuleFor(x => x.Tier).NotEmpty().MaximumLength(50));
        When(x => x.CompanySizeRange is not null, () =>
            RuleFor(x => x.CompanySizeRange)
                .Matches(@"^\d+(-\d+|\+)$").WithMessage("CompanySizeRange must be like '1-50' or '201+'."));
        When(x => x.ModuleKeys is not null, () =>
            RuleFor(x => x.ModuleKeys).NotEmpty().WithMessage("At least one module key is required."));
        When(x => x.Currency is not null, () =>
            RuleFor(x => x.Currency).NotEmpty().Length(3));
        When(x => x.TrialPeriodDays is not null, () =>
            RuleFor(x => x.TrialPeriodDays).GreaterThanOrEqualTo(0));
        When(x => x.UnpaidGracePeriodDays is not null, () =>
            RuleFor(x => x.UnpaidGracePeriodDays).GreaterThanOrEqualTo(0));
    }
}
