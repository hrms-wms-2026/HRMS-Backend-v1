using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public sealed class WorkAreaChangeRequest : ITenantOwnedEntity
{
    public const string WorkAreaOnsite = "onsite";
    public const string WorkAreaRemote = "remote";
    public const string WorkAreaEither = "either";
    public const string WorkAreaField = "field";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusCancelled = "cancelled";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LegalEntityId { get; set; }
    public DateOnly Date { get; set; }
    public string CurrentExpectedWorkArea { get; set; } = string.Empty;
    public string RequestedWorkArea { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
}
