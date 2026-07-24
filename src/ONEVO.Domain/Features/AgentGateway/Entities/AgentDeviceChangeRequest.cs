using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

/// <summary>
/// HR-reviewed request to replace an employee's currently approved desktop
/// with a newly enrolled desktop.
/// </summary>
public class AgentDeviceChangeRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CurrentAgentId { get; set; }
    public Guid RequestedAgentId { get; set; }

    /// <summary>pending | approved | rejected | cancelled | expired</summary>
    public string Status { get; set; } = "pending";

    public string? Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
