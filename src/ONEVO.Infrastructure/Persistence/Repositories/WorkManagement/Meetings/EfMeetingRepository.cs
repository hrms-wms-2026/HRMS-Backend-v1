using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingRepository : IMeetingRepository
{
    // Meeting tasks are hidden from every ordinary task query; only meeting reads that already passed MeetingVisibility
    // or the access resolver ignore that one filter (tenant scoping stays).
    private static readonly string[] ExcludeMeetingsOnly = [WorkTaskQueryFilters.ExcludeMeetings];

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

    public async Task<IReadOnlyList<MeetingSeries>> ListSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking().Where(s => s.TenantId == tenantId && seriesIds.Contains(s.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventParticipant>> ListParticipantsForSeriesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.MeetingSeriesId != null && seriesIds.Contains(p.MeetingSeriesId.Value))
            .ToListAsync(ct);

    public async Task<MeetingOccurrenceRow?> GetOccurrenceByTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await Rows(_db.MeetingDetails.Where(d => d.TenantId == tenantId && d.TaskId == taskId)).FirstOrDefaultAsync(ct);

    public async Task<MeetingOccurrenceRow?> GetOccurrenceAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await Rows(_db.MeetingDetails.Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.OriginalStart == key))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingOccurrenceRow>> ListVisibleAsync(Guid tenantId, Guid callerEmployeeId, DateTimeOffset from,
        DateTimeOffset to, Guid? projectId, Guid? seriesId, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var occurrences = MeetingVisibility.ForCaller(_db.MeetingDetails, _db, callerEmployeeId)
            .Where(d => d.TenantId == tenantId && d.StartAt < toUtc && d.EndAt > fromUtc);
        if (seriesId is Guid series)
            occurrences = occurrences.Where(d => d.SeriesId == series);
        if (projectId is Guid project)
            occurrences = occurrences.Where(d => _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly)
                .Any(t => t.Id == d.TaskId && t.ProjectId == project));
        return await Rows(occurrences.OrderBy(d => d.StartAt)).ToListAsync(ct);
    }

    public async Task<bool> IsSuppressedAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await _db.MeetingOccurrenceSuppressions.AnyAsync(
            s => s.TenantId == tenantId && s.SeriesId == seriesId && s.OriginalStart == key, ct);
    }

    // The task side of an occurrence: meeting tasks are hidden from every ordinary task query (ExcludeMeetings), so this
    // join - and only meeting reads that already passed MeetingVisibility or the access resolver - ignores that filter.
    private IQueryable<MeetingOccurrenceRow> Rows(IQueryable<MeetingDetails> occurrences)
        => occurrences.AsNoTracking().Join(
            _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly),
            d => d.TaskId, t => t.Id,
            (d, t) => new MeetingOccurrenceRow(d, t.ShortId, t.Title, t.Description, t.ProjectId, t.ObjectiveId));

    public async Task<MeetingSeries?> GetSeriesTrackedAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.MeetingSeries.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

    public async Task<MeetingDetails?> GetDetailsTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.MeetingDetails.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.TaskId == taskId, ct);

    public async Task<WorkTask?> GetMeetingTaskTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId && t.TaskKind == WorkTaskKinds.Meeting, ct);

    public async Task<IReadOnlyList<(MeetingDetails Details, WorkTask Task)>> ListTemplateOccurrencesTrackedAsync(
        Guid tenantId, Guid seriesId, DateTimeOffset after, CancellationToken ct = default)
    {
        var afterUtc = after.ToUniversalTime();
        var rows = await _db.MeetingDetails
            .Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.StartAt > afterUtc && !d.IsDetached && d.CancelledAt == null)
            .Join(_db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly), d => d.TaskId, t => t.Id, (d, t) => new { d, t })
            .OrderBy(x => x.d.StartAt)
            .ToListAsync(ct);
        return rows.Select(x => (x.d, x.t)).ToList();
    }

    public void AddEditLog(TaskEditLog log) => _db.TaskEditLogs.Add(log);

    public void AddSeries(MeetingSeries series) => _db.MeetingSeries.Add(series);

    public void AddParticipants(IEnumerable<CalendarEventParticipant> participants) => _db.CalendarEventParticipants.AddRange(participants);

    public void AddGuests(IEnumerable<CalendarEventGuest> guests) => _db.CalendarEventGuests.AddRange(guests);
}
