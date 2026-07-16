using FluentValidation;

namespace ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;

public class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEqual(Guid.Empty);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(50).WithMessage("Role name must be 50 characters or fewer.")
            .Matches("^[A-Za-z0-9 _-]+$")
            .WithMessage("Role name may contain letters, digits, spaces, underscores, and hyphens only.");

        RuleFor(x => x.Description)
            .MaximumLength(255).WithMessage("Description must be 255 characters or fewer.");
    }
}
