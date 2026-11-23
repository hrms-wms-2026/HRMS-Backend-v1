using FluentValidation;
using ONEVO.Application.Features.OrgStructure.Commands.DepartmentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;

public class UpdateDepartmentCommandValidator : DepartmentInputValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x)
            .Must(x => x.ParentDepartmentId == null || x.ParentDepartmentId != x.DepartmentId)
            .WithMessage("Department cannot be its own parent.");
    }
}
