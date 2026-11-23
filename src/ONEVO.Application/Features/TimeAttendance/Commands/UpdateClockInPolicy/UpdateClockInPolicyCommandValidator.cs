using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Validation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;

public class UpdateClockInPolicyCommandValidator : ClockInPolicyInputValidator<UpdateClockInPolicyCommand>
{
    public UpdateClockInPolicyCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty().WithMessage("Policy ID is required.");
    }
}
