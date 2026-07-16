using FluentValidation;

namespace ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;

public class AssignRolePermissionsCommandValidator : AbstractValidator<AssignRolePermissionsCommand>
{
    public AssignRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEqual(Guid.Empty);

        RuleFor(x => x.PermissionIds)
            .NotNull().WithMessage("Permission ids list is required (use an empty array to clear).");

        RuleForEach(x => x.PermissionIds)
            .NotEqual(Guid.Empty).WithMessage("Permission ids must not be empty.");
    }
}
