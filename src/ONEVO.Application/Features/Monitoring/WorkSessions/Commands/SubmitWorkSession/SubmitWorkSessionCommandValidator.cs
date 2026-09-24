using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.Commands.SubmitWorkSession;

public class SubmitWorkSessionCommandValidator : AbstractValidator<SubmitWorkSessionCommand>
{
    public SubmitWorkSessionCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleFor(x => x.ClockOutAt)
            .GreaterThanOrEqualTo(x => x.ClockInAt)
            .WithMessage("ClockOutAt cannot be before ClockInAt.");

        RuleFor(x => x.AccumulatedBreakSeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AccumulatedWorkSeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BreakSessionCount).GreaterThanOrEqualTo(0);

        When(x => x.ScheduleDisplay is not null, () =>
        {
            RuleFor(x => x.ScheduleDisplay!).MaximumLength(100);
        });
    }
}
