namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

// The /api/v1/meetings contract (meetings-as-tasks Phase 2, Plan B Task 1; docs/api/meetings-v1.md). Plan C adds the
// link and attendance members. Serialized with JsonSerializerDefaults.Web (camelCase).

/// <summary>Derived meeting status (spec D10) - never stored.</summary>
public static class MeetingStatuses
{
    public const string Scheduled = "scheduled";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>Which occurrences an edit, cancel or move applies to (spec 5.3, 5.5).</summary>
public static class MeetingScopes
{
    public const string This = "this";
    public const string Following = "following";
    public const string All = "all";
}

public static class MeetingRoles
{
    public const string Organizer = "organizer";
    public const string Attendee = "attendee";
}

/// <param name="ProjectId">Null creates an Office meeting (Calendar, needs calendar:write).</param>
/// <param name="Platform">"microsoft_teams", "zoom" or null.</param>
public sealed record CreateMeetingRequest(
    Guid? ProjectId, Guid? ObjectiveId, string Title, string? Description, string? Location, bool IsPrivate,
    DateTimeOffset Start, int DurationMinutes, bool IsAllDay, string Timezone, string? RecurrenceRule,
    IReadOnlyList<Guid> ParticipantEmployeeIds, IReadOnlyList<string> GuestEmails, string? Platform)
{
    // A client that leaves a list out sends null; it means "nobody", not a server error.
    public IReadOnlyList<Guid> ParticipantEmployeeIds { get; init; } = ParticipantEmployeeIds ?? [];
    public IReadOnlyList<string> GuestEmails { get; init; } = GuestEmails ?? [];
}

/// <param name="Reason">"inactive" or "not_found".</param>
public sealed record SkippedParticipant(Guid EmployeeId, string Reason);

/// <param name="Outcome">"created" (direct) or "requested" (a meeting.create approval request).</param>
/// <param name="Link">Set when a platform was chosen: the link's state right after create (usually "provisioning").</param>
public sealed record MeetingCreatedResponse(
    string Outcome, Guid? SeriesId, Guid? FirstOccurrenceTaskId, Guid? ApprovalRequestId,
    IReadOnlyList<SkippedParticipant> Skipped, MeetingLinkResponse? Link = null);

/// <summary>Link states beyond the provider row's own statuses (CalendarEventMeetingStatuses).</summary>
public static class CalendarMeetingLinkStates
{
    /// <summary>The organizer has no usable connection for the platform: the meeting exists without a link (P1-5).</summary>
    public const string NotConnected = "not_connected";
}

/// <summary>A series' Teams/Zoom link as its organizer sees it (attendees only ever get <see cref="JoinUrl"/>).</summary>
/// <param name="Status">A CalendarEventMeetingStatuses value, or <see cref="CalendarMeetingLinkStates.NotConnected"/>.</param>
/// <param name="ReleasePending">The series let the link go but another series or an open attendance check still needs it,
/// so the provider meeting is not cancelled yet.</param>
public sealed record MeetingLinkResponse(Guid? LinkId, string Provider, string Status, string? JoinUrl, string? OrganizerJoinUrl,
    string? FailureReason, int Generation, bool ReleasePending = false);

/// <summary>POST /series/{id}/link: a link for a series without one, or - with <paramref name="Regenerate"/> - a new link
/// for this series in place of its active one.</summary>
/// <param name="Platform">"microsoft_teams" or "zoom".</param>
public sealed record MeetingLinkRequest(string Platform, bool Regenerate = false);

/// <summary>GET /series/{id}/link. Attendees get only <see cref="JoinUrl"/> (set while the link is active); the organizer
/// also gets <see cref="Link"/>. Both are null when the series has no link.</summary>
public sealed record MeetingSeriesLinkResponse(
    string? JoinUrl,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    MeetingLinkResponse? Link);

/// <summary>An occurrence's attendance progress after a recheck (spec 5.8).</summary>
/// <param name="Status">A MeetingAttendanceStatuses value.</param>
/// <param name="LastError">Why the last check could not read the provider (provider_error, missing_scope, ...), if it failed.</param>
public sealed record MeetingAttendanceStatusResponse(string Status, DateTimeOffset? NextCheckAt, DateTimeOffset? FinalizedAt, string? LastError);

/// <summary>GET .../occurrences/{originalStart}/attendance (spec 5.8). Attendees get only their own row and no
/// <see cref="Unmatched"/>.</summary>
/// <param name="Status">A MeetingAttendanceStatuses value.</param>
/// <param name="Display">"Unknown" (pending, attendance_unavailable, sync_failed), "Recorded" (provisional, synced) or
/// "Not applicable" (no link).</param>
/// <param name="Unmatched">Organizer only: sessions of the link that fit no occurrence.</param>
public sealed record MeetingAttendanceResponse(string Status, string Display, IReadOnlyList<MeetingAttendanceRow> Rows,
    IReadOnlyList<MeetingUnmatchedSession>? Unmatched);

/// <param name="Kind">"employee" or "guest".</param>
/// <param name="AttendedSeconds">The union of the person's intervals; null without any recorded join.</param>
/// <param name="Outcome">"joined", "no_join_recorded" (only once synced - never "absent") or "unknown".</param>
public sealed record MeetingAttendanceRow(string Kind, Guid? EmployeeId, string? Email, string DisplayName, int? AttendedSeconds, string Outcome);

public sealed record MeetingUnmatchedSession(DateTimeOffset Start, DateTimeOffset? End);

/// <summary>Values of <see cref="MeetingAttendanceRow.Outcome"/> and <see cref="MeetingAttendanceResponse.Display"/>.</summary>
public static class MeetingAttendanceOutcomes
{
    public const string Joined = "joined";
    public const string NoJoinRecorded = "no_join_recorded";
    public const string Unknown = "unknown";

    public const string DisplayUnknown = "Unknown";
    public const string DisplayRecorded = "Recorded";
    public const string DisplayNotApplicable = "Not applicable";
}

public sealed record MeetingPersonRef(Guid EmployeeId, string Name);

/// <param name="Kind">"employee" or "guest".</param>
/// <param name="Source">"series" or "occurrence" (a per-occurrence add).</param>
public sealed record MeetingAttendeeView(
    string Kind, Guid? EmployeeId, string? Name, string? Email, string? Response, string? ResponseReason, string Source);

public sealed record MeetingPlacementView(Guid ProjectId, string ProjectName, Guid ObjectiveId, string ModuleTitle, bool IsOffice);

public sealed record MeetingOccurrenceResponse(
    Guid TaskId, string ShortId, Guid SeriesId, DateTimeOffset OriginalStart, DateTimeOffset StartAt,
    DateTimeOffset EndAt, bool IsAllDay, string Timezone, string Title, string? Description, string? Location,
    bool IsPrivate, string Status, bool IsDetached, int Revision, int SeriesRevision, string? RecurrenceRule,
    MeetingPlacementView Placement, MeetingPersonRef Organizer, string MyRole, string? MyResponse,
    IReadOnlyList<MeetingAttendeeView> Attendees, bool CanEdit);

public sealed record MeetingListItem(
    Guid TaskId, string ShortId, Guid SeriesId, DateTimeOffset OriginalStart, DateTimeOffset StartAt,
    DateTimeOffset EndAt, bool IsAllDay, string Title, string Status, bool IsPrivate, Guid ProjectId,
    Guid ObjectiveId, string MyRole, string? MyResponse);

public sealed record MeetingSeriesResponse(
    Guid SeriesId, string Title, string? Description, string? Location, bool IsPrivate, DateTimeOffset DtStart,
    int DurationMinutes, bool IsAllDay, string Timezone, string? RecurrenceRule, bool IsCancelled, int Revision,
    MeetingPlacementView Placement, MeetingPersonRef Organizer, string MyRole,
    IReadOnlyList<MeetingAttendeeView> Attendees, IReadOnlyList<MeetingListItem> UpcomingOccurrences);

/// <param name="Scope"><see cref="MeetingScopes"/>.</param>
/// <param name="Revision">The occurrence revision for "this", the series revision otherwise.</param>
public sealed record EditMeetingRequest(
    string Scope, int Revision, string? Title, string? Description, string? Location, bool? IsPrivate,
    DateTimeOffset? Start, int? DurationMinutes, bool? IsAllDay, string? Timezone, string? RecurrenceRule);

public sealed record MeetingEditResponse(Guid SeriesId, Guid OccurrenceTaskId, bool Split);

public sealed record CancelMeetingRequest(string Scope);

/// <param name="Scope">"this" or "following".</param>
public sealed record MoveMeetingRequest(string Scope, Guid ProjectId, Guid ObjectiveId);

/// <param name="Outcome">"moved" or "requested".</param>
public sealed record MeetingMoveResponse(string Outcome, Guid? SeriesId, Guid? ApprovalRequestId);

public sealed record MeetingAttendeesRequest(IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<string> GuestEmails)
{
    // A left-out list means "nobody", as in CreateMeetingRequest.
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = EmployeeIds ?? [];
    public IReadOnlyList<string> GuestEmails { get; init; } = GuestEmails ?? [];
}

public sealed record MeetingAttendeesResponse(int Added, IReadOnlyList<SkippedParticipant> Skipped);

/// <param name="Status">"accepted" or "rejected".</param>
/// <param name="OriginalStart">Set to answer for one occurrence the caller was added to individually.</param>
public sealed record RespondToMeetingRequest(string Status, string? Reason, DateTimeOffset? OriginalStart);

/// <summary>What the approver of a meeting.create sees, frozen at submit time (D5). Never the link, attendance or action items.</summary>
public sealed record MeetingApprovalSnapshot(
    string Title, string? Description, DateTimeOffset Start, int DurationMinutes, string Timezone, string RecurrenceSummary,
    string OrganizerName, string ModulePath, IReadOnlyList<string> ParticipantNames, IReadOnlyList<string> GuestEmails,
    string? Platform);

/// <summary>The meeting.create approval payload: the full form, applied as submitted, and the approver's snapshot.</summary>
public sealed record MeetingCreatePayload(CreateMeetingRequest Form, MeetingApprovalSnapshot Snapshot);
