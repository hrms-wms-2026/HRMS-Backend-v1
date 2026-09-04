using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public enum BiometricProfileStatus { Enrolled, Failed }

/// <summary>One enrolled biometric profile per employee. Unique on (tenant_id, employee_id).</summary>
public class BiometricProfile : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public BiometricProfileStatus Status { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }

    /// <summary>file_records.Id of the reference photo captured at enrollment. Null until Task 3 lands / enrollment completes with a reference image.</summary>
    public Guid? ReferencePhotoFileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
