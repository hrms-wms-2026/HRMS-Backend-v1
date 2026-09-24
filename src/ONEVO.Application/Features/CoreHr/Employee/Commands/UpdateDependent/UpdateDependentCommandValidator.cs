using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;

public class UpdateDependentCommandValidator : AbstractValidator<UpdateDependentCommand>
{
    private static readonly string[] AllowedRelationships = ["spouse", "child", "parent", "other"];

    public UpdateDependentCommandValidator()
    {
        RuleFor(x => x.DependentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).Must(r => AllowedRelationships.Contains(r))
            .WithMessage("Relationship must be one of: spouse, child, parent, other.");
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}
