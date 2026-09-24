using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public class CheckDepartmentArchiveDependenciesQueryValidator
    : AbstractValidator<CheckDepartmentArchiveDependenciesQuery>
{
    public CheckDepartmentArchiveDependenciesQueryValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
