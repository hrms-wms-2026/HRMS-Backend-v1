using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

public static class MeetingSeriesLinkOrigins
{
    public const string Created = "created";
    public const string InheritedOnSplit = "inherited_on_split";
}

/// <summary>
/// A series' reference to a provider meeting link. Links are reference-counted, never transferred: a split series
/// inherits a reference to its parent's link, and a link is released only when its last reference is (spec 5.7).
/// At most one reference per series is active (<see cref="ReleasedAt"/> null).
/// </summary>
public class MeetingSeriesLink : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SeriesId { get; set; }
    public Guid CalendarEventMeetingId { get; set; }
    public string Origin { get; set; } = MeetingSeriesLinkOrigins.Created;
    public DateTimeOffset AttachedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
}
