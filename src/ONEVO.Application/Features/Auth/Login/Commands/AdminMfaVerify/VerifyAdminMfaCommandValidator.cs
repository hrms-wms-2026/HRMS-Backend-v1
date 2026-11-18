using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaVerify;

/// <summary>Task 19. A malformed code can never verify, so it is rejected before the challenge is looked up.</summary>
public sealed class VerifyAdminMfaCommandValidator : AbstractValidator<VerifyAdminMfaCommand>
{
    public VerifyAdminMfaCommandValidator()
    {
        RuleFor(x => x.Challenge).ValidName(AuthInputRules.ChallengeMaxLength);
        RuleFor(x => x.Code).ValidTotpCode();
    }
}
