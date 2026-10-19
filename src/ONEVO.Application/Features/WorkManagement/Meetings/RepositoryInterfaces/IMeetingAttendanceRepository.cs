using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>
/// Per-occurrence attendance of series-owned links (spec 5.8): provider sessions (<c>meeting_attendance_sessions</c>) and
/// their records (<c>calendar_event_meeting_attendances</c> rows that carry a session). Rows of calendar-event links are
/// never read or written here. Never calls SaveChangesAsync.
/// </summary>
public interface IMeetingAttendanceRepository
{
    /// <summary>Series-owned links with at least one occurrence whose check is due: not cancelled, not finalized,
    /// <c>attendance_next_check_at &lt;= now</c>.</summary>
    Task<IReadOnlyList<Guid>> ListDueLinkIdsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>The link's due occurrences (see <see cref="ListDueLinkIdsAsync"/>), tracked.</summary>
    Task<IReadOnlyList<MeetingDetails>> ListDueOccurrencesTrackedAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>The link's non-cancelled occurrences overlapping [from, to], untracked - the candidates for session matching.</summary>
    Task<IReadOnlyList<MeetingDetails>> ListLinkOccurrencesAsync(Guid tenantId, Guid linkId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Every session recorded for the link, tracked.</summary>
    Task<IReadOnlyList<MeetingAttendanceSession>> ListSessionsTrackedAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    void AddSession(MeetingAttendanceSession session);

    /// <summary>The records of these sessions, tracked.</summary>
    Task<IReadOnlyList<CalendarEventMeetingAttendance>> ListRecordsTrackedAsync(Guid tenantId, IReadOnlyCollection<Guid> sessionIds,
        CancellationToken ct = default);

    void AddRecord(CalendarEventMeetingAttendance record);

    /// <summary>The occurrence's attendance records, untracked.</summary>
    Task<IReadOnlyList<CalendarEventMeetingAttendance>> ListOccurrenceRecordsAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default);

    /// <summary>The link's sessions that fit no occurrence, oldest first, untracked.</summary>
    Task<IReadOnlyList<MeetingAttendanceSession>> ListUnmatchedSessionsAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);

    /// <summary>Employees by email, lower-cased: an employee's work email and the account email of each of their calendar
    /// connections (the C2 engine's identity rule). An email two employees share resolves to neither.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ResolveEmployeesByEmailAsync(Guid tenantId, IReadOnlyCollection<string> emails,
        CancellationToken ct = default);

    /// <summary><c>pg_try_advisory_xact_lock</c> on <c>link:{linkId}</c>: false while another sync of the link runs.
    /// Needs a transaction.</summary>
    Task<bool> TryLockLinkSyncAsync(Guid tenantId, Guid linkId, CancellationToken ct = default);
}
