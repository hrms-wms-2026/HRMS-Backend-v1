using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

/// <summary>
/// An employee's request to move their registered remote work location (EmployeeWorkLocation) to
/// a new point. Deliberately a separate table from WorkAreaChangeRequest: that entity is scoped
/// to a single specific Date (a one-day onsite/remote override), while this represents a
/// persistent "this is where I work from now" change with no date at all.
/// </summary>
public sealed class LocationChangeRequest : ITenantOwnedEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusCancelled = "cancelled";

    // Reached only once the employee opts in ("save as new location?" -> yes) on some later
    // clock-in after approval. Until then the request stays "approved" and the tray keeps
    // re-prompting on every clock-in - saying no is "not this time", not a rejection.
    public const string StatusApplied = "applied";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LegalEntityId { get; set; }
    public double RequestedLatitude { get; set; }
    public double RequestedLongitude { get; set; }
    public double? RequestedAccuracyMeters { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
