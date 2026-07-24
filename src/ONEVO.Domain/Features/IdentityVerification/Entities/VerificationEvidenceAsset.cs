using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.IdentityVerification.Entities;

public class VerificationEvidenceAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? VerificationRecordId { get; set; }
    public Guid? PresenceSessionId { get; set; }
    public Guid? AttendanceEventId { get; set; }
    public Guid? BiometricEventId { get; set; }
    public Guid FileRecordId { get; set; }

    /// <summary>
    /// identity_verification_photo | clock_in_photo | clock_out_photo |
    /// verification_failure_photo
    /// </summary>
    public string EvidenceType { get; set; } = "identity_verification_photo";

    /// <summary>on_demand | clock_in | clock_out | absence_detected</summary>
    public string TriggerType { get; set; } = "clock_in";

    public DateTimeOffset CapturedAt { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? BiometricDeviceId { get; set; }
    public Guid? RetentionPolicyId { get; set; }
    public Guid? LegalHoldId { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
