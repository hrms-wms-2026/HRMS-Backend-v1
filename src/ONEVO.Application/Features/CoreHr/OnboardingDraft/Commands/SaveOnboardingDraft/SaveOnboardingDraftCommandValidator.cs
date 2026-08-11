using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandValidator : AbstractValidator<SaveOnboardingDraftCommand>
{
    public SaveOnboardingDraftCommandValidator()
    {
        RuleFor(c => c.FirstName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .MaximumLength(100);
        RuleFor(c => c.LastName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .MaximumLength(100);
        RuleFor(c => c.WorkEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(c => c.LegalEntityId).NotEmpty();
        RuleFor(c => c.EmploymentType).NotEmpty().MaximumLength(30);
        RuleFor(c => c.StartDate).NotEqual(default(DateOnly));
        RuleFor(c => c.LastSavedStep).NotEmpty().MaximumLength(50);
        RuleFor(c => c.EmployeeNumber).MaximumLength(20);
        RuleFor(c => c.WorkModeId).GreaterThan(0);
    }
}
