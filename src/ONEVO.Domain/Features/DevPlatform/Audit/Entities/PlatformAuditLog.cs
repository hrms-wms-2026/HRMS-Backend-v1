namespace ONEVO.Domain.Features.DevPlatform.Audit.Entities;

/// <summary>
/// Append-only record of a privileged Developer Platform action (canonical `platform_audit_logs`).
/// Deliberately NOT an ITenantOwnedEntity: a platform action may have no tenant (role changes,
/// credential rotation) or target one tenant from outside it (TargetTenantId). The runtime database
/// role has no UPDATE or DELETE on this table.
/// BeforeJson/AfterJson hold an explicit, reviewed projection per command - never a serialized
/// command or entity, and never a secret value (a rotation records that it happened, not the value).
/// </summary>
public class PlatformAuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorPlatformUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public Guid? TargetTenantId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
