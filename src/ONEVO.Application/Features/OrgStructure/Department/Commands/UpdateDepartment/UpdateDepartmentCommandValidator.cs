using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;

public class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(100).WithMessage("Department name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Department code cannot exceed 20 characters.")
            .Must(code => CodePattern.IsMatch(code!.Trim()))
            .WithMessage("Department code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x)
            .Must(x => x.ParentDepartmentId == null || x.ParentDepartmentId != x.DepartmentId)
            .WithMessage("Department cannot be its own parent.");
    }
}
