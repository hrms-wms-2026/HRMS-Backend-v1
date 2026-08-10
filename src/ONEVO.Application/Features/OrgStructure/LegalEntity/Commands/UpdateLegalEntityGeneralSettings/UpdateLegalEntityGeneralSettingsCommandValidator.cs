using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;

public class UpdateLegalEntityGeneralSettingsCommandValidator
    : AbstractValidator<UpdateLegalEntityGeneralSettingsCommand>
{
    private static readonly string[] AllowedTimeFormats = ["12h", "24h"];
    private static readonly string[] AllowedStatuses = ["active", "inactive"];

    public UpdateLegalEntityGeneralSettingsCommandValidator()
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

        RuleFor(x => x.Timezone)
            .NotEmpty().WithMessage("Invalid timezone selected.")
            .MaximumLength(50).WithMessage("Invalid timezone selected.");

        RuleFor(x => x.FinancialYearStartMonth)
            .InclusiveBetween(1, 12)
            .WithMessage("Financial year start month must be between 1 and 12.");

        RuleFor(x => x.FirstDayOfWeek)
            .InclusiveBetween(1, 7)
            .WithMessage("First day of week must be between 1 and 7.");

        RuleFor(x => x.StandardWorkingDays)
            .NotEmpty().WithMessage("At least one standard working day is required.")
            .Must(days => days.All(d => d is >= 1 and <= 7))
            .WithMessage("Standard working days must contain valid weekday values.")
            .Must(days => days.Distinct().Count() == days.Count)
            .WithMessage("Standard working days must not contain duplicates.");

        RuleFor(x => x.DefaultLanguage)
            .NotEmpty().WithMessage("Default language is required.")
            .MaximumLength(10).WithMessage("Default language must be 10 characters or fewer.");

        RuleFor(x => x.DateFormat)
            .NotEmpty().WithMessage("Date format is required.")
            .MaximumLength(20).WithMessage("Date format must be 20 characters or fewer.");

        RuleFor(x => x.TimeFormat)
            .Must(AllowedTimeFormats.Contains)
            .WithMessage("Time format must be '12h' or '24h'.");

        RuleFor(x => x.Status)
            .Must(s => AllowedStatuses.Contains(s?.ToLowerInvariant()))
            .WithMessage("Status must be 'active' or 'inactive'.");

        RuleFor(x => x.TaxRegistrationNumber)
            .MaximumLength(80).WithMessage("Tax registration number must be 80 characters or fewer.")
            .When(x => x.TaxRegistrationNumber is not null);

        RuleFor(x => x.VatGstNumber)
            .MaximumLength(50).WithMessage("VAT/GST number must be 50 characters or fewer.")
            .When(x => x.VatGstNumber is not null);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email address is invalid.")
            .MaximumLength(254).WithMessage("Email address must be 254 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Phone number is invalid.")
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.Website)
            .MaximumLength(255).WithMessage("Website URL is invalid.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("Website URL is invalid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Website));
    }
}
