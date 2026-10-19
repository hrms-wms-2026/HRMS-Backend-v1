using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

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

    Task<IReadOnlyList<MeetingSeries>> ListSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default);

    /// <summary>Current participants of several series at once.</summary>
    Task<IReadOnlyList<CalendarEventParticipant>> ListParticipantsForSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default);

    Task<MeetingOccurrenceRow?> GetOccurrenceByTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    Task<MeetingOccurrenceRow?> GetOccurrenceAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default);

    /// <summary>The materialized occurrences overlapping [from, to) that the caller may see (MeetingVisibility), by start.</summary>
    Task<IReadOnlyList<MeetingOccurrenceRow>> ListVisibleAsync(Guid tenantId, Guid callerEmployeeId, DateTimeOffset from, DateTimeOffset to,
        Guid? projectId, Guid? seriesId, CancellationToken ct = default);

    Task<bool> IsSuppressedAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default);

    void AddSeries(MeetingSeries series);
    void AddParticipants(IEnumerable<CalendarEventParticipant> participants);
    void AddGuests(IEnumerable<CalendarEventGuest> guests);
}
