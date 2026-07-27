using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Validation;

/// <summary>
/// Single source of truth for password strength rules. Originally lived only in
/// AcceptInvitationPasswordCommandValidator; extracted here so invite signup, password reset,
/// and force-change-password all enforce the identical rule set rather than drifting apart.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(this IRuleBuilder<T, string> ruleBuilder)
        => ruleBuilder
            .NotEmpty()
            .MinimumLength(MinimumLength)
            .WithMessage($"Password must be at least {MinimumLength} characters.");
}
