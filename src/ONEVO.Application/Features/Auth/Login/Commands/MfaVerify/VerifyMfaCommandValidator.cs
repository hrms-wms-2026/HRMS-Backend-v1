using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;

/// <summary>Task 19. A malformed code can never verify, so it is rejected before the handler looks up the challenge.
/// IpAddress and UserAgent are copied into sessions (nvarchar(45) / nvarchar(500)).</summary>
public sealed class VerifyMfaCommandValidator : AbstractValidator<VerifyMfaCommand>
{
    public VerifyMfaCommandValidator()
    {
        RuleFor(x => x.MfaChallenge).ValidName(AuthInputRules.ChallengeMaxLength);
        RuleFor(x => x.Code).ValidTotpCode();
        RuleFor(x => x.IpAddress).ValidName(AuthInputRules.IpAddressMaxLength, required: false);
        RuleFor(x => x.UserAgent).ValidName(AuthInputRules.UserAgentMaxLength, required: false);
    }
}
