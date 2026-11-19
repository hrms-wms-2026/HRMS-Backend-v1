using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

/// <summary>
/// One clock-in/clock-out face check from the tray. Consecutive failures decide when the
/// employee is let through anyway (with their manager alerted to review the photo).
/// </summary>
public class FaceVerificationAttempt : ITenantOwnedEntity
{
    public const string OutcomePassed = "passed";
    public const string OutcomeFailed = "failed";

    /// <summary>Failed again after the allowed retries, let through, manager alerted.</summary>
    public const string OutcomeOverridden = "overridden";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>"clock_in" or "clock_out".</summary>
    public string Purpose { get; set; } = string.Empty;

    public string Outcome { get; set; } = OutcomeFailed;

    /// <summary>Backend failure code for a failed or overridden attempt (e.g. not_matched).</summary>
    public string? FailureReason { get; set; }

    public float? SimilarityScore { get; set; }

    /// <summary>file_records.Id of the photo kept for the manager's review (failed and overridden attempts).</summary>
    public Guid? PhotoFileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
