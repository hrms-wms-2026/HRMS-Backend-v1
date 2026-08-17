using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Invite token issued during tenant provisioning. The token is stored only as a
/// SHA-256 hash; the plaintext is delivered through the email boundary once and is
/// never persisted.
/// </summary>
public class InvitationToken : ITenantOwnedEntity
{
    public const string EmployeeOnboardingPurpose = "employee_onboarding";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? PositionId { get; set; }
    public string Purpose { get; set; } = "general";
    public Guid? LegalEntityId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? PositionAssignmentId { get; set; }
    public Guid? OnboardingDraftId { get; set; }

    public string InvitedEmail { get; set; } = string.Empty;
    public string InvitedFullName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";

    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
    public string? CompletedWith { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedById { get; set; }

    public string? CompletionMethodsJson { get; set; }
    public bool? AllowGoogleEmailMismatch { get; set; }
    public string? AllowedEmailDomainsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedById { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        UsedAt is null && RevokedAt is null && ExpiresAt > now;
}
