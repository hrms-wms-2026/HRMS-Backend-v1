using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.IdentityVerification.Entities;

public class VerificationRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }

    /// <summary>photo | biometric | on_demand_photo</summary>
    public string Method { get; set; } = "photo";

    public decimal? MatchConfidence { get; set; }

    /// <summary>pending_review | verified | failed | skipped | expired</summary>
    public string Status { get; set; } = "pending_review";

    public Guid? AgentId { get; set; }
    public Guid? BiometricDeviceId { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>on_demand | clock_in | clock_out | absence_detected | biometric_scan</summary>
    public string Trigger { get; set; } = "clock_in";

    public Guid? RequestedById { get; set; }
    public Guid? AlertId { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? ResponseDurationSeconds { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
