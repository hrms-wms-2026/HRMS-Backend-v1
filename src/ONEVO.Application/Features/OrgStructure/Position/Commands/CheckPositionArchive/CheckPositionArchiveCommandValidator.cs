using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public class CheckPositionArchiveCommandValidator : AbstractValidator<CheckPositionArchiveCommand>
{
    public CheckPositionArchiveCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
