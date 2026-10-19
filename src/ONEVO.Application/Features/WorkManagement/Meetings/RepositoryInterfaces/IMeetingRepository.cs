using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>A materialized occurrence with the task fields it shows (title and agenda live on the task, spec 4.2).</summary>
public sealed record MeetingOccurrenceRow(MeetingDetails Details, string ShortId, string Title, string? Description, Guid ProjectId, Guid ObjectiveId);

/// <summary>Reads and writes of meeting series, occurrences and their attendees (meetings-as-tasks Phase 2). Occurrence
/// inserts and remaps go through <see cref="IMeetingOccurrenceWriter"/>. Never calls SaveChangesAsync.</summary>
public interface IMeetingRepository
{
    Task<MeetingSeries?> GetSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    /// <summary>The materialized occurrence at (series, original_start), or null while it is still virtual.</summary>
    Task<MeetingDetails?> GetDetailsAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default);

    /// <summary>Current (not removed) series participants.</summary>
    Task<IReadOnlyList<CalendarEventParticipant>> ListSeriesParticipantsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    Task<IReadOnlyList<CalendarEventGuest>> ListSeriesGuestsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingOccurrenceAttendee>> ListOverridesAsync(Guid tenantId, IReadOnlyCollection<Guid> occurrenceTaskIds, CancellationToken ct = default);

    /// <summary>The series an approved meeting.create already produced (idempotent apply).</summary>
    Task<MeetingSeries?> FindSeriesByApprovalRequestAsync(Guid tenantId, Guid approvalRequestId, CancellationToken ct = default);

    /// <summary>Recurring, not cancelled series - the generator's work list.</summary>
    Task<IReadOnlyList<Guid>> ListActiveRecurringSeriesIdsAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingSeries>> ListSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default);

    /// <summary>Current participants of several series at once.</summary>
    Task<IReadOnlyList<CalendarEventParticipant>> ListParticipantsForSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default);

    Task<MeetingOccurrenceRow?> GetOccurrenceByTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    Task<MeetingOccurrenceRow?> GetOccurrenceAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default);

    /// <summary>Every materialized, non-cancelled occurrence starting in [from, to), by start - the start reminders' input. No
    /// visibility filter: the caller notifies each occurrence's own attendees.</summary>
    Task<IReadOnlyList<MeetingOccurrenceRow>> ListStartingBetweenAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>The materialized occurrences overlapping [from, to) that the caller may see (MeetingVisibility), by start.</summary>
    Task<IReadOnlyList<MeetingOccurrenceRow>> ListVisibleAsync(Guid tenantId, Guid callerEmployeeId, DateTimeOffset from, DateTimeOffset to,
        Guid? projectId, Guid? seriesId, CancellationToken ct = default);

    /// <summary>True when the occurrence's task was soft-deleted (a split surplus): the occurrence is gone, though its
    /// meeting_details row keeps the key taken.</summary>
    Task<bool> IsOccurrenceRemovedAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default);

    Task<bool> IsSuppressedAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default);

    Task<MeetingSeries?> GetSeriesTrackedAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    Task<MeetingDetails?> GetDetailsTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>The meeting task itself (meeting tasks are hidden from every ordinary task query).</summary>
    Task<WorkTask?> GetMeetingTaskTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>The series' materialized occurrences that still follow its template: not started after
    /// <paramref name="after"/>, not detached, not cancelled - with their tasks, tracked.</summary>
    Task<IReadOnlyList<(MeetingDetails Details, WorkTask Task)>> ListTemplateOccurrencesTrackedAsync(
        Guid tenantId, Guid seriesId, DateTimeOffset after, CancellationToken ct = default);

    /// <summary>Every materialized occurrence of the series with original_start at or after <paramref name="from"/> (no
    /// tracking); soft-deleted ones are left out.</summary>
    Task<IReadOnlyList<MeetingDetails>> ListOccurrencesFromAsync(Guid tenantId, Guid seriesId, DateTimeOffset from, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingOccurrenceSuppression>> ListSuppressionsFromAsync(Guid tenantId, Guid seriesId, DateTimeOffset from, CancellationToken ct = default);

    /// <summary>True when the occurrence carries attendee overrides, action items or attendance rows - it must then be
    /// cancelled, never deleted (spec 5.5).</summary>
    Task<bool> HasDependentsAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default);

    void AddSuppression(MeetingOccurrenceSuppression suppression);

    /// <summary>Soft-deletes a meeting task (its meeting_details row stays, so the occurrence key stays taken).</summary>
    void RemoveMeetingTask(WorkTask task);

    /// <summary>The series participant row for this employee, including a removed (soft-deleted) one to revive.</summary>
    Task<CalendarEventParticipant?> GetSeriesParticipantTrackedIncludingRemovedAsync(Guid tenantId, Guid seriesId, Guid employeeId, CancellationToken ct = default);

    Task<CalendarEventGuest?> GetSeriesGuestTrackedAsync(Guid tenantId, Guid seriesId, string email, CancellationToken ct = default);

    /// <summary>This person's override on one occurrence (at most one exists).</summary>
    Task<MeetingOccurrenceAttendee?> GetOverrideTrackedAsync(Guid tenantId, Guid occurrenceTaskId, Guid? employeeId, string? email, CancellationToken ct = default);

    /// <summary>Deletes this person's <paramref name="change"/> overrides on the series' occurrences starting after
    /// <paramref name="after"/>.</summary>
    Task DeleteFutureOverridesAsync(Guid tenantId, Guid seriesId, Guid? employeeId, string? email, string change, DateTimeOffset after, CancellationToken ct = default);

    void AddOverride(MeetingOccurrenceAttendee row);
    void RemoveOverride(MeetingOccurrenceAttendee row);
    void RemoveParticipant(CalendarEventParticipant participant);
    void RemoveGuest(CalendarEventGuest guest);
    void AddRsvpHistory(CalendarEventParticipantRsvpHistory row);

    /// <summary>Moves every meeting task of the project off <paramref name="fromStatusId"/> onto <paramref name="toStatusId"/>.
    /// A meeting's status_id is only a placeholder (its status is derived, D10), so a status that only meetings use can
    /// still be deleted (final-review I7). Runs immediately (bulk update).</summary>
    Task RepointPlaceholderStatusAsync(Guid tenantId, Guid projectId, Guid fromStatusId, Guid toStatusId, CancellationToken ct = default);

    void AddEditLog(TaskEditLog log);

    void AddSeries(MeetingSeries series);
    void AddParticipants(IEnumerable<CalendarEventParticipant> participants);
    void AddGuests(IEnumerable<CalendarEventGuest> guests);
}
