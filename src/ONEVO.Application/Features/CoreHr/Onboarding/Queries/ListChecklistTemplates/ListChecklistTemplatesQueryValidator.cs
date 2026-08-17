using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistTemplates;

public class ListChecklistTemplatesQueryValidator : AbstractValidator<ListChecklistTemplatesQuery>
{
    public ListChecklistTemplatesQueryValidator()
    {
        RuleFor(q => q.LegalEntityId).NotEmpty();
        RuleFor(q => q.TemplateType).Must(t => t is null or "onboarding" or "offboarding")
            .WithMessage("templateType must be 'onboarding' or 'offboarding' when provided.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
