using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

/// <summary>How a meeting series came to exist (meetings-as-tasks Phase 2 spec 4.1, 5.2).</summary>
public static class MeetingCreationPaths
{
    public const string Direct = "direct";
    public const string Approval = "approval";
    public const string CalendarOffice = "calendar_office";
}

/// <summary>Why a series was split off another one (spec 5.5). Moves are forward-only.</summary>
public static class MeetingSplitReasons
{
    public const string EditFollowing = "edit_following";
    public const string MoveForward = "move_forward";
    public const string MoveSingle = "move_single";
}

/// <summary>
/// A meeting's series: the only placement authority for its occurrences (project + module), the template for
/// occurrences that are not materialized yet, and the owner of series attendees and links (spec 4.1, 4.2).
/// A null <see cref="RecurrenceRule"/> is a one-off meeting.
/// </summary>
public class MeetingSeries : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid OrganizerEmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public bool IsPrivate { get; set; }
    public DateTimeOffset DtStart { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsAllDay { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string? RecurrenceRule { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string CreationPath { get; set; } = MeetingCreationPaths.Direct;
    public Guid? ApprovalRequestId { get; set; }
    public Guid? SplitFromSeriesId { get; set; }
    public string? SplitReason { get; set; }
    public DateTimeOffset? SplitAt { get; set; }
    /// <summary>Optimistic concurrency token; bumped on every series write.</summary>
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
