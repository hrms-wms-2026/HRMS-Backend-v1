using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveManualCoverageRecord;

public class RemoveManualCoverageRecordCommandValidator : AbstractValidator<RemoveManualCoverageRecordCommand>
{
    public RemoveManualCoverageRecordCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
        RuleFor(x => x.CoverageId).NotEmpty().WithMessage("Coverage ID is required.");
    }
}
