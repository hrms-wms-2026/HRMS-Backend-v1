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

    public const int AdminMinimumLength = 12;
    public const int AdminMaximumLength = 64;

    /// <summary>
    /// Stricter than ApplyPasswordPolicy: Platform Admin accounts require a 12-64 character
    /// password per the MFA journey security review. Kept separate from the shared tenant
    /// policy so tenant behaviour never changes.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyAdminPasswordPolicy<T>(this IRuleBuilder<T, string> ruleBuilder)
        => ruleBuilder
            .NotEmpty()
            .MinimumLength(AdminMinimumLength)
            .WithMessage($"Password must be at least {AdminMinimumLength} characters.")
            .MaximumLength(AdminMaximumLength)
            .WithMessage($"Password must be at most {AdminMaximumLength} characters.");
}
