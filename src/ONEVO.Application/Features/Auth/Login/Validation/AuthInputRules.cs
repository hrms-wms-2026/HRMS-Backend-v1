using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.Auth.Login.Validation;

/// <summary>Task 19 bounds shared by the auth validators.</summary>
public static class AuthInputRules
{
    /// <summary>Emails are nvarchar(255) on users, employees and platform users.</summary>
    public const int EmailMaxLength = 255;
    /// <summary>Raw challenge tokens are 64 random bytes in base64 (88 characters); only their hash is stored.</summary>
    public const int ChallengeMaxLength = 512;
    /// <summary>sessions and legal_acceptance_records: ip_address nvarchar(45), user_agent nvarchar(500).</summary>
    public const int IpAddressMaxLength = 45;
    public const int UserAgentMaxLength = 500;

    /// <summary>A TOTP code is exactly six digits (OtpNetTotpService rejects anything else).</summary>
    public static IRuleBuilderOptions<T, string?> ValidTotpCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(code => code is { Length: 6 } && code.All(char.IsAsciiDigit))
            .WithMessage("'{PropertyName}' must be a 6-digit code.");

    /// <summary>A login email: required, at most 255 characters, in address form.</summary>
    public static IRuleBuilderOptions<T, string?> ValidLoginEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.ValidName(EmailMaxLength).EmailAddress();
}
