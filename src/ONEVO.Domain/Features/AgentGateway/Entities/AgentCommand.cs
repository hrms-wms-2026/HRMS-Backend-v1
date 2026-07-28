using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

public sealed class AgentCommand : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ActivitySnapshotId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";

    /// <summary>pending | accepted | denied | completed | failed | expired</summary>
    public string Status { get; set; } = "pending";

    /// <summary>allow | deny</summary>
    public string? Decision { get; set; }

    public string? ConsentNoticeVersion { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? MonitoringEvidenceAssetId { get; set; }
    public string? ResultCode { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}

