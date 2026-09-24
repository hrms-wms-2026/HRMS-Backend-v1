using FluentValidation;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public class CloneLeavePolicyCommandValidator : AbstractValidator<CloneLeavePolicyCommand>
{
    public CloneLeavePolicyCommandValidator()
    {
        RuleFor(x => x.SourcePolicyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100)
            .WithMessage("Country is required to determine statutory compliance");
        RuleFor(x => x.LegalEntityIds).NotEmpty()
            .WithMessage("Assign one or more legal entities.");
        RuleFor(x => x.LegalEntityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same legal entity cannot appear twice in one policy.");
    }
}
