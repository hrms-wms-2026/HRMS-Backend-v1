using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class MonitoringEvidenceAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? AgentDeviceId { get; set; }
    public Guid? ActivitySnapshotId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Guid FileRecordId { get; set; }

    /// <summary>screenshot | app_snapshot | idle_evidence</summary>
    public string EvidenceType { get; set; } = string.Empty;

    /// <summary>on_demand | auto_deviation</summary>
    public string TriggerType { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
