using FluentValidation;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandValidator : AbstractValidator<SaveOnboardingDraftCommand>
{
    public SaveOnboardingDraftCommandValidator()
    {
        // Saving is deliberately lenient (company + step only); full validation runs at finalize.
        RuleFor(c => c.FirstName).MaximumLength(100);
        RuleFor(c => c.LastName).MaximumLength(100);
        RuleFor(c => c.WorkEmail).EmailAddress().MaximumLength(320).When(c => !string.IsNullOrWhiteSpace(c.WorkEmail));
        RuleFor(c => c.LegalEntityId).NotEmpty();
        RuleFor(c => c.EmploymentType).MaximumLength(30);
        RuleFor(c => c.LastSavedStep).NotEmpty().MaximumLength(50);
        RuleFor(c => c.EmployeeNumber)
            .MaximumLength(EmployeeNumberRules.MaxLength)
            .Must(value => value is null || EmployeeNumberRules.IsValidFormat(EmployeeNumberRules.NormalizeInput(value)!))
            .WithMessage(EmployeeNumberRules.InvalidFormatMessage)
            .When(c => !string.IsNullOrWhiteSpace(c.EmployeeNumber));
        RuleFor(c => c.WorkModeId).NotEqual(Guid.Empty).When(c => c.WorkModeId is not null);
    }
}
