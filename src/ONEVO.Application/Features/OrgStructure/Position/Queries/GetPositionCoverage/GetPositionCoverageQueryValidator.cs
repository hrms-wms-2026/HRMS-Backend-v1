using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionCoverage;

public class GetPositionCoverageQueryValidator : AbstractValidator<GetPositionCoverageQuery>
{
    public GetPositionCoverageQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
