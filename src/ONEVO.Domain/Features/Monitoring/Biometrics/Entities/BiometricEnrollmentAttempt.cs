using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public enum BiometricEnrollmentStatus { Pending, Succeeded, Failed, Expired }

/// <summary>
/// One AWS Rekognition Face Liveness session lifecycle. AwsSessionId is opaque to us -
/// it is only ever passed back to Rekognition, never interpreted locally.
/// </summary>
public class BiometricEnrollmentAttempt : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public string AwsSessionId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ChallengeType { get; set; } = string.Empty;
    public BiometricEnrollmentStatus Status { get; set; }
    public float? Confidence { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
