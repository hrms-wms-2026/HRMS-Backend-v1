using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

public enum ExceptionType { SustainedLowActivity, AttendanceIrregularity, UnusualActivityPattern }
public enum ExceptionStatus { Open, Acknowledged, Resolved, Escalated }

/// <summary>
/// A multi-day pattern-detection case, distinct from the single-moment Notification
/// entity - carries a resolution lifecycle instead of being fire-and-forget.
/// </summary>
public class Exception : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public ExceptionType Type { get; set; }
    public ExceptionStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedById { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
}
