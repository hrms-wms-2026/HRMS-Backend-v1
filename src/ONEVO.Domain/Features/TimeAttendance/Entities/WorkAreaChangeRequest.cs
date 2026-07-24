using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class WorkAreaChangeRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LegalEntityId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? ShiftAssignmentId { get; set; }
    public string CurrentExpectedWorkArea { get; set; } = "onsite";
    public string RequestedWorkArea { get; set; } = "remote";
    public string Reason { get; set; } = string.Empty;

    /// <summary>pending | approved | rejected | cancelled</summary>
    public string Status { get; set; } = "pending";

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
