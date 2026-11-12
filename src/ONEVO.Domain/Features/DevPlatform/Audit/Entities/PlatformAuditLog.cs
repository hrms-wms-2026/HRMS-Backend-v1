namespace ONEVO.Domain.Features.DevPlatform.Audit.Entities;

/// <summary>
/// Append-only record of one privileged action taken through the Platform Admin API
/// (canonical <c>platform_audit_logs</c>). Platform-wide, so deliberately not
/// <c>ITenantOwnedEntity</c>; <see cref="TargetTenantId"/> only says which tenant the action touched.
/// Rows are never updated or deleted: the runtime database login is denied UPDATE and DELETE on the
/// table (ops/sqlserver/post-migration-grants.sql).
/// BeforeJson / AfterJson hold explicit, reviewed projections only. Never passwords, secrets, API
/// keys, tokens or raw request bodies.
/// </summary>
public class PlatformAuditLog
{
    public const string OutcomeSucceeded = "succeeded";
    public const string OutcomeFailed = "failed";

    public Guid Id { get; set; }

    /// <summary>The platform user who acted. Null for a system caller (for example tray release ingest).</summary>
    public Guid? ActorPlatformUserId { get; set; }

    /// <summary>Dotted action code, for example <c>platform_role.permissions_updated</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public Guid? TargetTenantId { get; set; }

    public string Outcome { get; set; } = OutcomeSucceeded;

    /// <summary>Short failure category (HTTP-style status or exception type), never an exception message.</summary>
    public string? FailureReason { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
