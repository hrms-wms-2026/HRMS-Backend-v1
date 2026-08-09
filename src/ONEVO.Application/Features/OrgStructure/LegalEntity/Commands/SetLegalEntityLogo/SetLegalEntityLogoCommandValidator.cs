using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public class SetLegalEntityLogoCommandValidator : AbstractValidator<SetLegalEntityLogoCommand>
{
    public SetLegalEntityLogoCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithMessage("File name is required.");
        RuleFor(x => x.ContentType).NotEmpty().WithMessage("Content type is required.");
    }
}
