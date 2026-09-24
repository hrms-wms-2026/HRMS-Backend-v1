using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;

public class CreateLegalEntityCommandValidator : AbstractValidator<CreateLegalEntityCommand>
{
    public CreateLegalEntityCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name must be 200 characters or fewer.");

        RuleFor(x => x.CompanyCode)
            .NotEmpty().WithMessage("Company code is required.")
            .MaximumLength(20).WithMessage("Company code must be 20 characters or fewer.");

        RuleFor(x => x.RegistrationNumber)
            .NotEmpty().WithMessage("Registration number is required.")
            .MaximumLength(50).WithMessage("Registration number must be 50 characters or fewer.");

        RuleFor(x => x.CountryCode)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(3).WithMessage("Country code must be an ISO 3166-1 alpha-3 code.");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty().WithMessage("Currency is required.")
            .MaximumLength(3).WithMessage("Currency code must be an ISO 4217 code.");

        RuleFor(x => x.TaxRegistrationNumber)
            .MaximumLength(80).WithMessage("Tax registration number must be 80 characters or fewer.")
            .When(x => x.TaxRegistrationNumber is not null);

        RuleFor(x => x.ParentLegalEntityId)
            .NotEqual(Guid.Empty).WithMessage("Parent company id must not be empty.")
            .When(x => x.ParentLegalEntityId is not null);
    }
}
