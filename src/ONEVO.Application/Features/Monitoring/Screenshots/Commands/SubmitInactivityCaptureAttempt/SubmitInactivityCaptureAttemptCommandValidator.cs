using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

public sealed class SubmitInactivityCaptureAttemptCommandValidator
    : AbstractValidator<SubmitInactivityCaptureAttemptCommand>
{
    public const int MinIdleDurationSeconds = 300;
    public const int MaxScreenshotBytes = 10 * 1024 * 1024;
    private const string JpegContentType = "image/jpeg";

    private static readonly HashSet<string> AllowedOutcomes =
    [
        InactivityCaptureOutcomes.Captured,
        InactivityCaptureOutcomes.Declined,
        InactivityCaptureOutcomes.TimedOut,
        InactivityCaptureOutcomes.ActivityResumed,
        InactivityCaptureOutcomes.MonitoringStopped,
        InactivityCaptureOutcomes.CaptureFailed
    ];

    public SubmitInactivityCaptureAttemptCommandValidator()
    {
        RuleFor(x => x.AttemptId)
            .NotEmpty();

        RuleFor(x => x.PolicyVersion)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Outcome)
            .NotEmpty()
            .Must(o => AllowedOutcomes.Contains(o))
            .WithMessage("Outcome must be one of: captured, declined, timed_out, activity_resumed, monitoring_stopped, capture_failed.");

        RuleFor(x => x.IdleDurationSeconds)
            .GreaterThanOrEqualTo(MinIdleDurationSeconds)
            .WithMessage($"IdleDurationSeconds must be at least {MinIdleDurationSeconds}.");

        RuleFor(x => x.MonitorCount)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => x.IdleStartedAt <= x.PromptedAt)
            .WithMessage("idle_started_at must be on or before prompted_at.");

        RuleFor(x => x)
            .Must(x => !x.DecisionAt.HasValue || x.PromptedAt <= x.DecisionAt.Value)
            .WithMessage("prompted_at must be on or before decision_at.");

        RuleFor(x => x)
            .Must(x => !x.DecisionAt.HasValue || !x.CapturedAt.HasValue || x.DecisionAt.Value <= x.CapturedAt.Value)
            .WithMessage("decision_at must be on or before captured_at.");

        When(x => x.Outcome == InactivityCaptureOutcomes.Captured, () =>
        {
            RuleFor(x => x.Content)
                .NotNull()
                .WithMessage("A JPEG file is required for captured outcomes.");

            RuleFor(x => x.FileSizeBytes)
                .NotNull()
                .GreaterThan(0)
                .LessThanOrEqualTo(MaxScreenshotBytes)
                .WithMessage($"Screenshot file must not exceed {MaxScreenshotBytes} bytes.");

            RuleFor(x => x.ContentType)
                .Equal(JpegContentType)
                .WithMessage("Captured screenshots must use content type image/jpeg.");

            RuleFor(x => x.MonitorCount)
                .GreaterThanOrEqualTo(1)
                .WithMessage("monitor_count must be at least 1 for captured outcomes.");

            RuleFor(x => x.DecisionAt)
                .NotNull()
                .WithMessage("decision_at is required for captured outcomes.");

            RuleFor(x => x.CapturedAt)
                .NotNull()
                .WithMessage("captured_at is required for captured outcomes.");

            RuleFor(x => x.Sha256)
                .NotEmpty()
                .WithMessage("sha256 is required for captured outcomes.");

            RuleFor(x => x.FailureCode)
                .Null()
                .WithMessage("failure_code must be omitted for captured outcomes.");
        });

        When(x => x.Outcome != InactivityCaptureOutcomes.Captured, () =>
        {
            RuleFor(x => x.Content)
                .Null()
                .WithMessage("File must not be included for non-captured outcomes.");

            RuleFor(x => x.FileSizeBytes)
                .Null()
                .WithMessage("File must not be included for non-captured outcomes.");
        });

        When(x => x.Outcome == InactivityCaptureOutcomes.CaptureFailed, () =>
        {
            RuleFor(x => x.FailureCode)
                .NotEmpty()
                .Must(code => InactivityCaptureFailureCodes.All.Contains(code!))
                .WithMessage("failure_code must be a known stable code for capture_failed outcomes.");
        });

        When(x => x.Outcome != InactivityCaptureOutcomes.CaptureFailed, () =>
        {
            RuleFor(x => x.FailureCode)
                .Null()
                .WithMessage("failure_code must be omitted unless outcome is capture_failed.");
        });
    }
}
