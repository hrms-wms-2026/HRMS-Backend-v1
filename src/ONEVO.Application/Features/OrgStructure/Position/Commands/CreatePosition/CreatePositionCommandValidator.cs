using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public class CreatePositionCommandValidator : AbstractValidator<CreatePositionCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,5}$", RegexOptions.Compiled);

    public CreatePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.DepartmentId).NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Position name is required.")
            .MaximumLength(100).WithMessage("Position name cannot exceed 100 characters.");

        // Split into two chains deliberately: a trailing .When() scopes to every preceding
        // rule in the same chain, not just the one immediately before it. Keeping NotEmpty
        // and MaximumLength unconditional (no .When()) is what makes "code is required" and
        // "code cannot exceed 5 characters" actually enforce; only the regex needs the guard,
        // since code.Trim() would NullReferenceException on a null Code otherwise.
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Position code is required.")
            .MaximumLength(5).WithMessage("Position code cannot exceed 5 characters.");

        RuleFor(x => x.Code)
            .Must(code => CodePattern.IsMatch(code.Trim()))
            .WithMessage("Position code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        // positionType is not user-selectable: the handler derives it from MaxOccupancy
        // (1 => unique, >1 => pooled), so validation here only needs to guard capacity itself.
        RuleFor(x => x.MaxOccupancy)
            .GreaterThanOrEqualTo(1).WithMessage("Capacity must be at least 1.");
    }
}
