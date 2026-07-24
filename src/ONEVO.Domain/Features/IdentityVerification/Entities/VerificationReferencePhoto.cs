using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.IdentityVerification.Entities;

public class VerificationReferencePhoto : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid PhotoFileId { get; set; }
    public string Source { get; set; } = "agent_first_sign_in"; // agent_first_sign_in | hr_verified_profile | admin_upload
    public string Status { get; set; } = "pending_review"; // pending_review | approved | rejected | replaced | revoked
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public bool IsActive { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
