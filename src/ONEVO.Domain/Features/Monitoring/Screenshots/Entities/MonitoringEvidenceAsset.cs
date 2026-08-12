using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

public class MonitoringEvidenceAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? AgentDeviceId { get; set; }
    public Guid? ActivitySnapshotId { get; set; }
    public Guid? AgentCommandId { get; set; }
    public Guid FileRecordId { get; set; }
    public string EvidenceType { get; set; } = "screenshot";
    public string Source { get; set; } = "agent";
    public string TriggerType { get; set; } = "on_demand";
    public Guid? RetentionPolicyId { get; set; }
    public Guid? LegalHoldId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
