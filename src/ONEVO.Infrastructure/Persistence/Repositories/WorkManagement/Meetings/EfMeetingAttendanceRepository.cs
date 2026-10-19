using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingAttendanceRepository : IMeetingAttendanceRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingAttendanceRepository(ApplicationDbContext db) => _db = db;

    private IQueryable<MeetingDetails> Due(Guid tenantId, DateTimeOffset now)
    {
        var at = now.ToUniversalTime();
        return _db.MeetingDetails.Where(d => d.TenantId == tenantId && d.CalendarEventMeetingId != null && d.CancelledAt == null
            && d.AttendanceFinalizedAt == null && d.AttendanceNextCheckAt != null && d.AttendanceNextCheckAt <= at
            && _db.CalendarEventMeetings.Any(m => m.Id == d.CalendarEventMeetingId && m.OwnerKind == CalendarMeetingOwnerKinds.MeetingSeries));
    }

    public async Task<IReadOnlyList<Guid>> ListDueLinkIdsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
        => await Due(tenantId, now).AsNoTracking().Select(d => d.CalendarEventMeetingId!.Value).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingDetails>> ListDueOccurrencesTrackedAsync(Guid tenantId, Guid linkId, DateTimeOffset now,
        CancellationToken ct = default)
        => await Due(tenantId, now).Where(d => d.CalendarEventMeetingId == linkId).OrderBy(d => d.StartAt).ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingDetails>> ListLinkOccurrencesAsync(Guid tenantId, Guid linkId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var (start, end) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.CalendarEventMeetingId == linkId && d.CancelledAt == null && d.EndAt >= start && d.StartAt <= end)
            .OrderBy(d => d.StartAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingAttendanceSession>> ListSessionsTrackedAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
        => await _db.MeetingAttendanceSessions.Where(s => s.TenantId == tenantId && s.CalendarEventMeetingId == linkId).ToListAsync(ct);

    public void AddSession(MeetingAttendanceSession session) => _db.MeetingAttendanceSessions.Add(session);

    public async Task<IReadOnlyList<CalendarEventMeetingAttendance>> ListRecordsTrackedAsync(Guid tenantId, IReadOnlyCollection<Guid> sessionIds,
        CancellationToken ct = default)
        => sessionIds.Count == 0
            ? []
            : await _db.CalendarEventMeetingAttendances
                .Where(a => a.TenantId == tenantId && a.AttendanceSessionId != null && sessionIds.Contains(a.AttendanceSessionId.Value))
                .ToListAsync(ct);

    public void AddRecord(CalendarEventMeetingAttendance record) => _db.CalendarEventMeetingAttendances.Add(record);

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveEmployeesByEmailAsync(Guid tenantId, IReadOnlyCollection<string> emails,
        CancellationToken ct = default)
    {
        var wanted = emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim().ToLowerInvariant()).Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<string, Guid>();

        var byWorkEmail = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Email != null && wanted.Contains(e.Email.ToLower()))
            .Select(e => new { Email = e.Email!.ToLower(), e.Id }).ToListAsync(ct);
        var byConnection = await (
                from c in _db.ExternalCalendarConnections.AsNoTracking()
                join e in _db.Employees.AsNoTracking() on c.UserId equals e.UserId
                where c.TenantId == tenantId && e.TenantId == tenantId && wanted.Contains(c.ExternalAccountEmail.ToLower())
                select new { Email = c.ExternalAccountEmail.ToLower(), e.Id })
            .ToListAsync(ct);

        // An address that points at two different employees is not evidence for either.
        return byWorkEmail.Concat(byConnection).Distinct()
            .GroupBy(x => x.Email)
            .Where(g => g.Select(x => x.Id).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
    }

    public async Task<bool> TryLockLinkSyncAsync(Guid tenantId, Guid linkId, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{nameof(TryLockLinkSyncAsync)} needs a transaction.");
        var acquired = await _db.Database.SqlQuery<bool>(
            $"SELECT pg_try_advisory_xact_lock(hashtextextended({tenantId.ToString()}::text || ':link:' || {linkId.ToString()}::text, 0)) AS \"Value\"")
            .ToListAsync(ct);
        return acquired.Count == 1 && acquired[0];
    }
}
