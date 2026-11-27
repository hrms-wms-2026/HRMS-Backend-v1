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
    public string EvidenceType { get; set; } = MonitoringEvidenceTypes.Screenshot;
    public string Source { get; set; } = "agent";
    public string TriggerType { get; set; } = MonitoringEvidenceTriggers.OnDemand;
    public Guid? RetentionPolicyId { get; set; }
    public Guid? LegalHoldId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Values of <see cref="MonitoringEvidenceAsset.EvidenceType"/> (task 39f: a check constraint reads them).</summary>
public static class MonitoringEvidenceTypes
{
    public const string Screenshot = "screenshot";
}

/// <summary>Values of <see cref="MonitoringEvidenceAsset.TriggerType"/> (task 39f: a check constraint reads them).</summary>
public static class MonitoringEvidenceTriggers
{
    public const string OnDemand = "on_demand";
    public const string Periodic = "periodic";
    public const string InactivityApproved = "inactivity_approved";
}
