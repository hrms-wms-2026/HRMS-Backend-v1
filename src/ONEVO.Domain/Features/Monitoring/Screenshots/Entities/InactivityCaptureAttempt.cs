using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

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
