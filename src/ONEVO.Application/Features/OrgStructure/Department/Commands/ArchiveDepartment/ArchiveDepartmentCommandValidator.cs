using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;

public class ArchiveDepartmentCommandValidator : AbstractValidator<ArchiveDepartmentCommand>
{
    public ArchiveDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
