using FluentValidation;

namespace ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;

public sealed class SwitchActiveCompanyCommandValidator : AbstractValidator<SwitchActiveCompanyCommand>
{
    public SwitchActiveCompanyCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
