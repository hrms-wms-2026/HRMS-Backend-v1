using FluentValidation;

namespace ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;

public sealed class InviteTenantAdminCommandValidator : AbstractValidator<InviteTenantAdminCommand>
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "google"
    };

    public InviteTenantAdminCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.CompletionMethods)
            .Must(methods => methods is null || methods.All(m => AllowedMethods.Contains(m)))
            .WithMessage("completionMethods must be a subset of {password, google}.");

        RuleForEach(x => x.AllowedEmailDomains)
            .MaximumLength(253);
    }
}
