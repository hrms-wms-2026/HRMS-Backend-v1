using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

/// <summary>
/// The trusted enrollment reference for CompareFaces during daily check-in (Plan 2).
/// Exactly one row per (TenantId, EmployeeId) may have Status == Active at a time —
/// enforced by a partial unique index (see EmployeeBiometricProfileConfiguration), not by
/// application logic alone.
/// </summary>
public class EmployeeBiometricProfile : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }

    public string Provider { get; set; } = "aws_rekognition";
    public string Region { get; set; } = "ap-south-1";

    /// <summary>R2 storage key of the private reference image (design: "private and encrypted in R2").</summary>
    public string ReferenceStorageKey { get; set; } = string.Empty;

    public string Status { get; set; } = BiometricProfileStatus.Active;

    public string ConsentVersion { get; set; } = string.Empty;
    public DateTimeOffset ConsentAcceptedAt { get; set; }

    public Guid EnrollmentAttemptId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}
