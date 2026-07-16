using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;

public class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Slug).MaximumLength(100)
            .Matches(@"^[a-z0-9-]+$").WithMessage("Slug may only contain lowercase letters, digits, and hyphens.")
            .When(x => x.Slug is not null);
        RuleFor(x => x.IndustryProfile).MaximumLength(30).When(x => x.IndustryProfile is not null);
    }
}
