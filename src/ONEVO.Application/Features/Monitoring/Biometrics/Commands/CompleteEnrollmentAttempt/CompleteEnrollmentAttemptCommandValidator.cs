using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public class CompleteEnrollmentAttemptCommandValidator : AbstractValidator<CompleteEnrollmentAttemptCommand>
{
    public CompleteEnrollmentAttemptCommandValidator()
    {
        RuleFor(x => x.AttemptId).NotEmpty();
    }
}
