using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingLinkRepository : IMeetingLinkRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingLinkRepository(ApplicationDbContext db) => _db = db;

    public async Task<CalendarEventMeeting?> GetActiveForSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AsNoTracking()
            .Where(m => m.TenantId == tenantId && _db.MeetingSeriesLinks.Any(
                l => l.CalendarEventMeetingId == m.Id && l.SeriesId == seriesId && l.ReleasedAt == null))
            .FirstOrDefaultAsync(ct);

    public async Task<CalendarEventMeeting?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == linkId, ct);

    public async Task<CalendarEventMeeting?> GetTrackedAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == linkId, ct);

    public async Task<Guid?> TryClaimForSeriesAsync(Guid tenantId, Guid seriesId, Guid connectionId, string provider, bool invitesPending,
        DateTimeOffset now, CancellationToken ct = default)
    {
        // The caller holds the series lock, so this check and the insert cannot interleave with another claim; the
        // one-active-reference unique index still backs it.
        if (await _db.MeetingSeriesLinks.AnyAsync(l => l.TenantId == tenantId && l.SeriesId == seriesId && l.ReleasedAt == null, ct))
            return null;
        var link = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = null, OwnerKind = CalendarMeetingOwnerKinds.MeetingSeries,
            ExternalCalendarConnectionId = connectionId, Provider = provider, Status = CalendarEventMeetingStatuses.Provisioning,
            LinkGeneration = 1, ProvisioningStartedAt = now, InvitesPending = invitesPending, CreatedAt = now,
        };
        _db.CalendarEventMeetings.Add(link);
        _db.MeetingSeriesLinks.Add(new MeetingSeriesLink
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SeriesId = seriesId, CalendarEventMeetingId = link.Id,
            Origin = MeetingSeriesLinkOrigins.Created, AttachedAt = now,
        });
        return link.Id;
    }

    public async Task<Guid?> AddInheritedReferenceAsync(Guid tenantId, Guid fromSeriesId, Guid toSeriesId, DateTimeOffset now, CancellationToken ct = default)
    {
        var linkId = await _db.MeetingSeriesLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.SeriesId == fromSeriesId && l.ReleasedAt == null)
            .Select(l => (Guid?)l.CalendarEventMeetingId).FirstOrDefaultAsync(ct);
        if (linkId is not Guid id)
            return null;
        _db.MeetingSeriesLinks.Add(new MeetingSeriesLink
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SeriesId = toSeriesId, CalendarEventMeetingId = id,
            Origin = MeetingSeriesLinkOrigins.InheritedOnSplit, AttachedAt = now,
        });
        return id;
    }

    public async Task<Guid?> ReleaseAsync(Guid tenantId, Guid seriesId, DateTimeOffset now, CancellationToken ct = default)
    {
        var released = await _db.Database.SqlQuery<Guid>($"""
            UPDATE meeting_series_links SET released_at = {now}
            WHERE tenant_id = {tenantId} AND series_id = {seriesId} AND released_at IS NULL
            RETURNING calendar_event_meeting_id AS "Value"
            """).ToListAsync(ct);
        return released.Count == 0 ? null : released[0];
    }

    public async Task<IReadOnlyList<Guid>> ListActiveReferencingSeriesAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
        => await _db.MeetingSeriesLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.CalendarEventMeetingId == linkId && l.ReleasedAt == null)
            .Select(l => l.SeriesId).ToListAsync(ct);

    public async Task<bool> AnyOccurrenceNeedsLinkAsync(Guid tenantId, Guid linkId, DateTimeOffset now, TimeSpan lateRecheckWindow,
        CancellationToken ct = default)
    {
        var at = now.ToUniversalTime();
        var recheckSince = at - lateRecheckWindow;
        return await _db.MeetingDetails.AsNoTracking().AnyAsync(d => d.TenantId == tenantId && d.CalendarEventMeetingId == linkId
            && d.CancelledAt == null
            && (d.EndAt > at || d.AttendanceFinalizedAt == null || d.EndAt > recheckSince), ct);
    }

    public async Task SetOccurrenceLinkAsync(Guid tenantId, Guid seriesId, Guid? linkId, DateTimeOffset fromStart, TimeSpan firstCheckpoint,
        CancellationToken ct = default)
    {
        var from = fromStart.ToUniversalTime();
        var occurrences = _db.MeetingDetails.Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.StartAt >= from);
        if (linkId is null)
            await occurrences.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.CalendarEventMeetingId, (Guid?)null)
                .SetProperty(d => d.AttendanceStatus, MeetingAttendanceStatuses.NotApplicable)
                .SetProperty(d => d.AttendanceNextCheckAt, (DateTimeOffset?)null)
                .SetProperty(d => d.AttendanceCheckpoint, 0), ct);
        else
            await occurrences.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.CalendarEventMeetingId, linkId)
                .SetProperty(d => d.AttendanceStatus, MeetingAttendanceStatuses.Pending)
                .SetProperty(d => d.AttendanceNextCheckAt, d => (DateTimeOffset?)(d.EndAt + firstCheckpoint))
                .SetProperty(d => d.AttendanceCheckpoint, 0)
                .SetProperty(d => d.AttendanceFinalizedAt, (DateTimeOffset?)null), ct);
    }

    public async Task<bool> TryClaimRetryAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Provisioning}, provisioning_started_at = {now}, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {linkId}
              AND status IN ({CalendarEventMeetingStatuses.ProvisioningUnknown}, {CalendarEventMeetingStatuses.Failed})
            """, ct) == 1;

    public async Task<IReadOnlyList<Guid>> ListReleaseCandidateLinkIdsAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.OwnerKind == CalendarMeetingOwnerKinds.MeetingSeries
                && m.Status == CalendarEventMeetingStatuses.Active
                && _db.MeetingSeriesLinks.Any(l => l.CalendarEventMeetingId == m.Id))
            .Select(m => m.Id).ToListAsync(ct);

    public async Task<Guid?> GetLatestReferencingSeriesAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
        => await _db.MeetingSeriesLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.CalendarEventMeetingId == linkId)
            .OrderBy(l => l.ReleasedAt == null ? 0 : 1).ThenByDescending(l => l.ReleasedAt)
            .Select(l => (Guid?)l.SeriesId).FirstOrDefaultAsync(ct);

    public async Task LockLinkAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{nameof(LockLinkAsync)} needs a transaction.");
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM calendar_event_meetings WHERE tenant_id = {tenantId} AND id = {linkId} FOR UPDATE", ct);
    }
}
