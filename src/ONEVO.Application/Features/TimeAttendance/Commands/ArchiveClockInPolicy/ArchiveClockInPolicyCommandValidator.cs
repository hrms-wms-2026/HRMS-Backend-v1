using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;

public class ArchiveClockInPolicyCommandValidator : AbstractValidator<ArchiveClockInPolicyCommand>
{
    public ArchiveClockInPolicyCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty();
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
