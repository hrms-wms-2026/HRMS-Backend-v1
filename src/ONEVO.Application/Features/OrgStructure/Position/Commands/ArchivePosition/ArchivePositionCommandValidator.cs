using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public class ArchivePositionCommandValidator : AbstractValidator<ArchivePositionCommand>
{
    public ArchivePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
