using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingRepository
{
    Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default);
    Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Active meetings for the given provider whose event has already ended and whose
    /// attendance has never been synced - {Teams,Zoom}AttendanceSyncJob's poll target. The
    /// provider filter keeps the two jobs from racing on each other's rows.</summary>
    Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default);

    /// <summary>The end of the meeting's event (null if the event is gone) - the attendance-report cutoff runs from it.</summary>
    Task<DateTimeOffset?> GetEventEndAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>The event's scheduled start and end (its first occurrence for a series); null when it is gone.</summary>
    Task<CalendarEventWindow?> GetEventWindowAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Untracked read of the event's meeting row (claims are decided by SQL, not by this snapshot).</summary>
    Task<CalendarEventMeeting?> GetByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>INSERT ... ON CONFLICT (calendar_event_id) DO NOTHING as provisioning. False = another request owns the row.</summary>
    Task<bool> TryInsertProvisioningAsync(CalendarEventMeeting row, CancellationToken ct = default);

    /// <summary>failed/cancelled -> provisioning with the next link generation. Null = not claimable (someone else won).</summary>
    Task<int?> TryClaimNewGenerationAsync(Guid tenantId, Guid meetingId, Guid connectionId, string provider, DateTimeOffset now,
        bool invitesPending = false, CancellationToken ct = default);

    /// <summary>provisioning older than <paramref name="staleBefore"/> -> provisioning_unknown.</summary>
    Task<bool> TryMarkStaleProvisioningUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>provisioning_unknown -> provisioning (Retry or Remove owns it now), same generation.</summary>
    Task<bool> TryClaimUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>provisioning_unknown -> unresolved.</summary>
    Task<bool> TryMarkUnknownUnresolvedAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default);

    void Update(CalendarEventMeeting meeting);

    /// <summary>Stages a write of ONLY the attendance-sync columns (synced / unavailable / failed, updated-at) of an
    /// untracked snapshot, so a sync job never writes back a status or link generation that changed
    /// after it read the row.</summary>
    void UpdateAttendanceSync(CalendarEventMeeting meeting);
}
