using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

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
}
