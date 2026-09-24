using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public class GetPositionTreeQueryValidator : AbstractValidator<GetPositionTreeQuery>
{
    public GetPositionTreeQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
    }
}
