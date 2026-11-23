using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaConfirmSetup;

/// <summary>Task 19. Enrollment is confirmed with one 6-digit TOTP code.</summary>
public sealed class ConfirmAdminMfaSetupCommandValidator : AbstractValidator<ConfirmAdminMfaSetupCommand>
{
    public ConfirmAdminMfaSetupCommandValidator()
    {
        RuleFor(x => x.Code).ValidTotpCode();
    }
}
