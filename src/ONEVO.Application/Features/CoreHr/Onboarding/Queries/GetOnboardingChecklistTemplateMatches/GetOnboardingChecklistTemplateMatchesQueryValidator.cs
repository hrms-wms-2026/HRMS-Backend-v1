using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.GetOnboardingChecklistTemplateMatches;

public class GetOnboardingChecklistTemplateMatchesQueryValidator : AbstractValidator<GetOnboardingChecklistTemplateMatchesQuery>
{
    public GetOnboardingChecklistTemplateMatchesQueryValidator()
    {
        RuleFor(q => q.LegalEntityId).NotEmpty();
    }
}
