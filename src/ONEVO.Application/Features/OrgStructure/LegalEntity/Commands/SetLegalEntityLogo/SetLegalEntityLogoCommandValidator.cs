using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public class SetLegalEntityLogoCommandValidator : AbstractValidator<SetLegalEntityLogoCommand>
{
    public SetLegalEntityLogoCommandValidator()
    {
        RuleFor(x => x.FileId)
            .NotEqual(Guid.Empty).WithMessage("File id is required.");
    }
}
