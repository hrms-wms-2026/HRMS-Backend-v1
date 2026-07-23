using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Tracks the currently logged-in employee on an enrolled device.
/// Only one active session per device (enforced by unique partial index).
/// Spec: modules/agent-gateway/overview.md — agent_sessions table.
/// </summary>
public class AgentSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;   // matches RegisteredAgent.DeviceId
    public Guid EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}
