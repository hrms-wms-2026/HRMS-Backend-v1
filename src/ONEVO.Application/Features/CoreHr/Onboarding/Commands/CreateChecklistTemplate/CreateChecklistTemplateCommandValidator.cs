using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;

public class CreateChecklistTemplateCommandValidator : AbstractValidator<CreateChecklistTemplateCommand>
{
    public CreateChecklistTemplateCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.TemplateType).Must(t => t is "onboarding" or "offboarding")
            .WithMessage("templateType must be 'onboarding' or 'offboarding'.");
        RuleFor(c => c.LegalEntityId).NotEmpty();
        RuleForEach(c => c.Tasks).ChildRules(task =>
        {
            task.RuleFor(t => t.Title).NotEmpty().MaximumLength(200);
            task.RuleFor(t => t.OwnerType).NotEmpty();
            task.RuleFor(t => t.DueOffsetDays).GreaterThanOrEqualTo(0);
        });
    }
}
