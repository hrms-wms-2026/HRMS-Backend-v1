namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Short-lived enrollment challenge created by enroll/start.
/// Not tenant-scoped because tenant is unknown until the browser session confirms.
/// Expires in 10 minutes. Deleted after completion.
/// </summary>
public class AgentEnrollmentChallenge
{
    public Guid Id { get; set; }                    // = enrollment_id returned to TrayApp
    public string DeviceId { get; set; } = string.Empty;   // UUID v7 from agent install
    public string DeviceName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>pending | confirmed | completed | expired</summary>
    public string Status { get; set; } = "pending";

    /// <summary>SHA-256 hash of the short-lived authorization_code. Set when status=confirmed.</summary>
    public string? AuthorizationCodeHash { get; set; }

    /// <summary>Set when the browser session confirms. Used by enroll/complete to write tenant-scoped rows.</summary>
    public Guid? TenantId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ConfirmedByUserId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
