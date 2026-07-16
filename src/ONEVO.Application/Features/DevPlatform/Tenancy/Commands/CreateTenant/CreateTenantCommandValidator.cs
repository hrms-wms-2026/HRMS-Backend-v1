using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;

public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug)
            .NotEmpty()
            .MinimumLength(3).WithMessage("Slug must be at least 3 characters.")
            .MaximumLength(63).WithMessage("Slug must be at most 63 characters.")
            .Matches(@"^[a-z0-9][a-z0-9\-]{1,61}[a-z0-9]$")
            .WithMessage("Slug must start and end with a lowercase letter or digit, and may only contain lowercase letters, digits, and hyphens.");
        RuleFor(x => x.IndustryProfile).NotEmpty().MaximumLength(30);
        RuleFor(x => x.CompanySizeRange).NotEmpty();
        RuleFor(x => x.LegalEntityName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(3);
        RuleFor(x => x.Timezone).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
    }
}
