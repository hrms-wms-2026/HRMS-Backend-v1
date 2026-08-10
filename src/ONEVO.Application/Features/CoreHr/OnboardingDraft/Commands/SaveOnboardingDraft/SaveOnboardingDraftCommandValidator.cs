using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandValidator : AbstractValidator<SaveOnboardingDraftCommand>
{
    public SaveOnboardingDraftCommandValidator()
    {
        RuleFor(c => c.EmployeeName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.WorkEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(c => c.LegalEntityId).NotEmpty();
        RuleFor(c => c.EmploymentType).NotEmpty().MaximumLength(30);
        RuleFor(c => c.StartDate).NotEqual(default(DateOnly));
        RuleFor(c => c.LastSavedStep).NotEmpty().MaximumLength(50);
        RuleFor(c => c.EmployeeNumber).MaximumLength(20);
    }
}
