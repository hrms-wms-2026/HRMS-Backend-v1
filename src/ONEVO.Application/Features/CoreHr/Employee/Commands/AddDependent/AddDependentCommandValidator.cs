using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;

public class AddDependentCommandValidator : AbstractValidator<AddDependentCommand>
{
    private static readonly string[] AllowedRelationships = ["spouse", "child", "parent", "other"];

    public AddDependentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).Must(r => AllowedRelationships.Contains(r))
            .WithMessage("Relationship must be one of: spouse, child, parent, other.");
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}
