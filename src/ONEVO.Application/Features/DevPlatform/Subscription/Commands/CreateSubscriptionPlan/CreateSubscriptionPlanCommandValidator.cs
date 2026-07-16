using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;

public class CreateSubscriptionPlanCommandValidator : AbstractValidator<CreateSubscriptionPlanCommand>
{
    public CreateSubscriptionPlanCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .Matches("^[a-z0-9_]+$").WithMessage("Code must be lowercase alphanumeric with underscores only.");
        RuleFor(x => x.Tier).NotEmpty().MaximumLength(50);
        RuleFor(x => x.CompanySizeRange).NotEmpty()
            .Matches(@"^\d+(-\d+|\+)$").WithMessage("CompanySizeRange must be like '1-50' or '201+'.");
        RuleFor(x => x.ModuleKeys).NotEmpty().WithMessage("At least one module key is required.");
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TrialPeriodDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UnpaidGracePeriodDays).GreaterThanOrEqualTo(0);
    }
}
