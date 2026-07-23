using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// Snapshot of agent health reported on each heartbeat.
/// Spec: modules/agent-gateway/overview.md — agent_health_logs table.
/// </summary>
public class AgentHealthLog : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public decimal CpuUsage { get; set; }
    public int MemoryMb { get; set; }
    public string ErrorsJson { get; set; } = "[]";
    public bool TamperDetected { get; set; } = false;
}
