using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Monitoring policy pushed to an agent after enrollment.
/// Spec: modules/agent-gateway/overview.md — agent_policies table.
/// </summary>
public class AgentPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }    // FK -> registered_agents.id
    public string PolicyJson { get; set; } = "{}";
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
