using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventParticipantStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string ResolutionRequested = "resolution_requested";
    public const string ReplacementNominated = "replacement_nominated";
}

public class CalendarEventParticipant : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ResponseStatus { get; set; } = CalendarEventParticipantStatuses.Pending;
    public string? ResponseReason { get; set; }
    /// <summary>When the participant last answered (null = never answered).</summary>
    public DateTimeOffset? RespondedAt { get; set; }
}

/// <summary>One RSVP change of a participant (plan C1 P1-1): who answered what, from what, when and why.</summary>
public class CalendarEventParticipantRsvpHistory : BaseEntity
{
    public Guid ParticipantId { get; set; }
    public Guid EventId { get; set; }
    public Guid EmployeeId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
    public string? Reason { get; set; }
}
