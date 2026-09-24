using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

/// <summary>
/// One inactivity-prompt/capture attempt reported by the Tray App's InactivityScreenshotCollector.
/// Identity (tenant/employee/device) is derived server-side from the Device JWT, never accepted
/// from the request — mirrors SubmitPeriodicScreenshotCommand.
/// </summary>
public record SubmitInactivityCaptureAttemptCommand(
    Guid AttemptId,
    string PolicyVersion,
    DateTimeOffset IdleStartedAt,
    DateTimeOffset PromptedAt,
    DateTimeOffset? DecisionAt,
    DateTimeOffset? CapturedAt,
    int IdleDurationSeconds,
    int MonitorCount,
    string Outcome,
    string? FailureCode,
    string? ContentType,
    string? Sha256,
    Stream? Content) : IRequest<Result<Guid>>;
