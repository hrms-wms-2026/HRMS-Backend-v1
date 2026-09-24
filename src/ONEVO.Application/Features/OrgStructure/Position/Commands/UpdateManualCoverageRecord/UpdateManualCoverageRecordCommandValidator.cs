using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord;

public class UpdateManualCoverageRecordCommandValidator : AbstractValidator<UpdateManualCoverageRecordCommand>
{
    public UpdateManualCoverageRecordCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
        RuleFor(x => x.CoverageId).NotEmpty().WithMessage("Coverage ID is required.");
        RuleFor(x => x.OwnerOrder).GreaterThanOrEqualTo(1).WithMessage("Owner order must be at least 1.");
    }
}
