using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>
/// Permission for one employee (the delegate) to run another employee's (the principal's) meetings as if they were the
/// principal. Active while it is not revoked, not past <see cref="ExpiresAt"/>, and both employees are active. A revoked
/// row is kept for audit.
/// </summary>
public class MeetingDelegate : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PrincipalEmployeeId { get; set; }
    public Guid DelegateEmployeeId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null means the grant lasts until it is revoked.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>"Can also plan my tasks" (2026-11-11): the delegate may also see the principal's tasks and plan them on the
    /// principal's planner. Off by default; only the principal sets it.</summary>
    public bool CanPlanTasks { get; set; }
}
