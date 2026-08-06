using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public class RestorePositionCommandValidator : AbstractValidator<RestorePositionCommand>
{
    public RestorePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
