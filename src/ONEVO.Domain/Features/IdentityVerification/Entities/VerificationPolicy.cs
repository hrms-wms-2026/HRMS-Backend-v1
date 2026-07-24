using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.IdentityVerification.Entities;

public class VerificationPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool RequirePhotoClockIn { get; set; }
    public bool RequirePhotoClockOut { get; set; }
    public bool CameraPhotoVerificationEnabled { get; set; }
    public bool AbsencePhotoCaptureEnabled { get; set; }

    /// <summary>remote_only | onsite_only | remote_and_onsite | disabled</summary>
    public string PhotoCaptureContextScope { get; set; } = "disabled";

    public decimal MatchThreshold { get; set; } = 80m;

    /// <summary>manual_review | trusted_sso_auto_approve</summary>
    public string ReferenceEnrollmentMode { get; set; } = "manual_review";

    public bool BlockMonitoringUntilReferenceApproved { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
