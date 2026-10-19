using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingRepository : IMeetingRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingRepository(ApplicationDbContext db) => _db = db;

    public async Task<MeetingSeries?> GetSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

    public async Task<MeetingDetails?> GetDetailsAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await _db.MeetingDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.OriginalStart == key, ct);
    }

    public async Task<IReadOnlyList<CalendarEventParticipant>> ListSeriesParticipantsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.MeetingSeriesId == seriesId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventGuest>> ListSeriesGuestsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.CalendarEventGuests.AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.MeetingSeriesId == seriesId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingOccurrenceAttendee>> ListOverridesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> occurrenceTaskIds, CancellationToken ct = default)
        => occurrenceTaskIds.Count == 0
            ? []
            : await _db.MeetingOccurrenceAttendees.AsNoTracking()
                .Where(a => a.TenantId == tenantId && occurrenceTaskIds.Contains(a.OccurrenceTaskId))
                .ToListAsync(ct);

    public async Task<MeetingSeries?> FindSeriesByApprovalRequestAsync(Guid tenantId, Guid approvalRequestId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ApprovalRequestId == approvalRequestId, ct);

    public void AddSeries(MeetingSeries series) => _db.MeetingSeries.Add(series);

    public void AddParticipants(IEnumerable<CalendarEventParticipant> participants) => _db.CalendarEventParticipants.AddRange(participants);

    public void AddGuests(IEnumerable<CalendarEventGuest> guests) => _db.CalendarEventGuests.AddRange(guests);
}
