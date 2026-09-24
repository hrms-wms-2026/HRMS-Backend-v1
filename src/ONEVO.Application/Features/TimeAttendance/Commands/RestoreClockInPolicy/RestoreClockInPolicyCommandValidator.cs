using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;

public class RestoreClockInPolicyCommandValidator : AbstractValidator<RestoreClockInPolicyCommand>
{
    public RestoreClockInPolicyCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty();
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
