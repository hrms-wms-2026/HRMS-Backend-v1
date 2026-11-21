using FluentValidation;
using ONEVO.Application.Features.OrgStructure.Commands.PositionValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public class UpdatePositionCommandValidator : PositionInputValidator<UpdatePositionCommand>
{
    public UpdatePositionCommandValidator()
    {
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");

        RuleFor(x => x)
            .Must(x => x.ReportsToPositionId == null || x.ReportsToPositionId != x.PositionId)
            .WithMessage("A position cannot report to itself.");
    }
}
