using FluentAssertions;
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Monitoring.Screenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class SubmitInactivityCaptureAttemptValidatorTests
{
    private readonly SubmitInactivityCaptureAttemptCommandValidator _sut = new();

    private static SubmitInactivityCaptureAttemptCommand ValidDeclined() =>
        ValidCommand(InactivityCaptureOutcomes.Declined);

    private static SubmitInactivityCaptureAttemptCommand ValidCaptured(Stream? content = null) =>
        ValidCommand(
            InactivityCaptureOutcomes.Captured,
            content ?? new MemoryStream([0xFF, 0xD8, 0xFF]),
            fileSizeBytes: 3,
            monitorCount: 2,
            decisionAt: BasePromptedAt.AddSeconds(3),
            capturedAt: BasePromptedAt.AddSeconds(5),
            sha256: "abc123");

    private static readonly DateTimeOffset BaseIdleStart = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
    private static readonly DateTimeOffset BasePromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z");

    private static SubmitInactivityCaptureAttemptCommand ValidCommand(
        string outcome,
        Stream? content = null,
        long? fileSizeBytes = null,
        int monitorCount = 0,
        DateTimeOffset? decisionAt = null,
        DateTimeOffset? capturedAt = null,
        string? sha256 = null,
        string? failureCode = null,
        int idleDurationSeconds = 300)
        => new(
            Guid.NewGuid(),
            "policy-1",
            BaseIdleStart,
            BasePromptedAt,
            decisionAt,
            capturedAt,
            idleDurationSeconds,
            monitorCount,
            outcome,
            failureCode,
            outcome == InactivityCaptureOutcomes.Captured ? "image/jpeg" : null,
            sha256,
            outcome == InactivityCaptureOutcomes.Captured ? 0 : null,
            outcome == InactivityCaptureOutcomes.Captured ? 0 : null,
            outcome == InactivityCaptureOutcomes.Captured ? 1920 : null,
            outcome == InactivityCaptureOutcomes.Captured ? 1080 : null,
            outcome == InactivityCaptureOutcomes.Captured ? "shot.jpg" : null,
            fileSizeBytes,
            content);

    [Theory]
    [InlineData(InactivityCaptureOutcomes.Captured)]
    [InlineData(InactivityCaptureOutcomes.Declined)]
    [InlineData(InactivityCaptureOutcomes.TimedOut)]
    [InlineData(InactivityCaptureOutcomes.ActivityResumed)]
    [InlineData(InactivityCaptureOutcomes.MonitoringStopped)]
    [InlineData(InactivityCaptureOutcomes.CaptureFailed)]
    public void Allowed_outcomes_pass_base_rules(string outcome)
    {
        var cmd = outcome switch
        {
            InactivityCaptureOutcomes.Captured => ValidCaptured(),
            InactivityCaptureOutcomes.CaptureFailed => ValidCommand(
                outcome, failureCode: InactivityCaptureFailureCodes.NoDisplays),
            _ => ValidCommand(outcome, decisionAt: BasePromptedAt.AddSeconds(2))
        };

        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Unknown_outcome_fails()
    {
        var cmd = ValidDeclined() with { Outcome = "unknown" };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Outcome);
    }

    [Fact]
    public void Idle_duration_below_300_fails()
    {
        var cmd = ValidDeclined() with { IdleDurationSeconds = 299 };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.IdleDurationSeconds);
    }

    [Fact]
    public void Timestamp_order_idle_after_prompted_fails()
    {
        var cmd = ValidDeclined() with { IdleStartedAt = BasePromptedAt.AddSeconds(1) };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Timestamp_order_prompted_after_decision_fails()
    {
        var cmd = ValidDeclined() with { DecisionAt = BasePromptedAt.AddSeconds(-1) };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Captured_without_file_fails()
    {
        var cmd = ValidCaptured(content: null) with { FileSizeBytes = null };
        _sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Captured_without_jpeg_content_type_fails()
    {
        var cmd = ValidCaptured() with { ContentType = "image/png" };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Captured_with_monitor_count_zero_fails()
    {
        var cmd = ValidCaptured() with { MonitorCount = 0 };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.MonitorCount);
    }

    [Fact]
    public void Non_captured_with_file_fails()
    {
        var cmd = ValidDeclined() with
        {
            Content = new MemoryStream([1]),
            FileSizeBytes = 1
        };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Content);
    }

    [Fact]
    public void Captured_file_over_10mb_fails()
    {
        var cmd = ValidCaptured() with { FileSizeBytes = SubmitInactivityCaptureAttemptCommandValidator.MaxScreenshotBytes + 1 };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FileSizeBytes);
    }

    [Fact]
    public void Capture_failed_with_unknown_failure_code_fails()
    {
        var cmd = ValidCommand(
            InactivityCaptureOutcomes.CaptureFailed,
            failureCode: "mystery_error");
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FailureCode);
    }

    [Theory]
    [InlineData(InactivityCaptureFailureCodes.SessionLocked)]
    [InlineData(InactivityCaptureFailureCodes.NoDisplays)]
    [InlineData(InactivityCaptureFailureCodes.ZeroBounds)]
    [InlineData(InactivityCaptureFailureCodes.CaptureApiFailed)]
    [InlineData(InactivityCaptureFailureCodes.CaptureTooLarge)]
    public void Capture_failed_with_known_failure_code_passes(string failureCode)
    {
        var cmd = ValidCommand(InactivityCaptureOutcomes.CaptureFailed, failureCode: failureCode);
        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Declined_with_failure_code_fails()
    {
        var cmd = ValidDeclined() with { FailureCode = InactivityCaptureFailureCodes.NoDisplays };
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FailureCode);
    }
}
