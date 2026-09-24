using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Commands.UpdateChecklistTemplate;

public class UpdateChecklistTemplateCommandValidator : AbstractValidator<UpdateChecklistTemplateCommand>
{
    public UpdateChecklistTemplateCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleForEach(c => c.Tasks).ChildRules(task =>
        {
            task.RuleFor(t => t.Title).NotEmpty().MaximumLength(200);
            task.RuleFor(t => t.OwnerType).NotEmpty();
            task.RuleFor(t => t.DueOffsetDays).GreaterThanOrEqualTo(0);
        });
    }
}
