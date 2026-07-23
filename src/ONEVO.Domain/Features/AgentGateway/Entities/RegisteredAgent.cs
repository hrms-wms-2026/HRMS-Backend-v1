using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// A desktop agent installed on an employee's machine.
/// Spec: modules/agent-gateway/overview.md — registered_agents table.
/// </summary>
public class RegisteredAgent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? EmployeeId { get; set; }    // nullable — set at employee login
    public string DeviceId { get; set; } = string.Empty;   // UUID v7 from agent install (unique per tenant)
    public string DeviceName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>active | inactive | revoked</summary>
    public string Status { get; set; } = "active";

    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
