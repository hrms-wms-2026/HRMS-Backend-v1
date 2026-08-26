using FluentValidation;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.AdjustEntitlement;

public class AdjustEntitlementCommandValidator : AbstractValidator<AdjustEntitlementCommand>
{
    public AdjustEntitlementCommandValidator()
    {
        RuleFor(x => x.EntitlementId).NotEmpty();
        RuleFor(x => x.TotalDays).GreaterThan(0);
        RuleFor(x => x.CarriedForwardDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
