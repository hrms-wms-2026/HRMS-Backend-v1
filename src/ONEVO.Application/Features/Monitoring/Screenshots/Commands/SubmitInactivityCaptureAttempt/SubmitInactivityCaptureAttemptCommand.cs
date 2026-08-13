using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

/// <summary>
/// Records one inactivity prompt/capture attempt from the tray agent.
/// Tenant, employee, and device identity are derived server-side from the Device JWT.
/// </summary>
public sealed record SubmitInactivityCaptureAttemptCommand(
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
    int? VirtualBoundsX,
    int? VirtualBoundsY,
    int? VirtualBoundsWidth,
    int? VirtualBoundsHeight,
    string? FileName,
    long? FileSizeBytes,
    Stream? Content) : IRequest<Result<SubmitInactivityCaptureAttemptResponse>>;
