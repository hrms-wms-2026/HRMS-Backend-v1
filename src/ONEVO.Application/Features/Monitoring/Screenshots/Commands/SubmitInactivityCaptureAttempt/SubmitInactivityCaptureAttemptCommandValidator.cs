using FluentValidation;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

public class SubmitInactivityCaptureAttemptCommandValidator : AbstractValidator<SubmitInactivityCaptureAttemptCommand>
{
    private static readonly string[] KnownFailureCodes =
        ["no_displays", "zero_bounds", "capture_too_large", "capture_api_failed"];

    public SubmitInactivityCaptureAttemptCommandValidator()
    {
        RuleFor(x => x.AttemptId).NotEmpty();
        RuleFor(x => x.PolicyVersion).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Outcome).Must(o => InactivityCaptureOutcomes.All.Contains(o))
            .WithMessage("Outcome must be one of: " + string.Join(", ", InactivityCaptureOutcomes.All));

        // Floor only — the real, tenant-configurable idle threshold (1-60 minutes) is checked
        // against the resolved policy in the handler, which needs tenant/employee context this
        // synchronous validator does not have.
        RuleFor(x => x.IdleDurationSeconds).GreaterThanOrEqualTo(60);
        RuleFor(x => x.MonitorCount).GreaterThanOrEqualTo(0);

        RuleFor(x => x.PromptedAt).GreaterThanOrEqualTo(x => x.IdleStartedAt);
        RuleFor(x => x.DecisionAt).GreaterThanOrEqualTo(x => x.PromptedAt)
            .When(x => x.DecisionAt.HasValue);
        RuleFor(x => x.CapturedAt).GreaterThanOrEqualTo(x => x.DecisionAt ?? x.PromptedAt)
            .When(x => x.CapturedAt.HasValue);

        RuleFor(x => x.FailureCode).Must(code => KnownFailureCodes.Contains(code))
            .When(x => !string.IsNullOrWhiteSpace(x.FailureCode))
            .WithMessage("Unknown failure code.");

        When(x => x.Outcome == InactivityCaptureOutcomes.Captured, () =>
        {
            RuleFor(x => x.Content).NotNull().WithMessage("A captured attempt must include the screenshot file.");
            RuleFor(x => x.MonitorCount).GreaterThanOrEqualTo(1);
            RuleFor(x => x.CapturedAt).NotNull();
        }).Otherwise(() =>
        {
            RuleFor(x => x.Content).Null().WithMessage("Only a captured attempt may include a screenshot file.");
        });
    }
}
