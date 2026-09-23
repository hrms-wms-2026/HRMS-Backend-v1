using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

public static class InactivityCaptureOutcomes
{
    public const string Captured = "captured";
    public const string Declined = "declined";
    public const string TimedOut = "timed_out";
    public const string ActivityResumed = "activity_resumed";
    public const string MonitoringStopped = "monitoring_stopped";
    public const string CaptureFailed = "capture_failed";

    public static readonly IReadOnlyCollection<string> All =
    [
        Captured, Declined, TimedOut, ActivityResumed, MonitoringStopped, CaptureFailed
    ];
}

/// <summary>
/// One inactivity-prompt/capture attempt lifecycle record submitted by the Tray App's
/// InactivityScreenshotCollector. <see cref="Id"/> is the client-generated attempt id, not
/// database-generated, so retries of the same attempt are idempotent lookups by primary key.
/// </summary>
public sealed class InactivityCaptureAttempt : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public Guid? WorkSessionId { get; set; }
    public DateTimeOffset IdleStartedAt { get; set; }
    public DateTimeOffset PromptedAt { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public int IdleDurationSeconds { get; set; }
    public int MonitorCount { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public Guid? EvidenceAssetId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
