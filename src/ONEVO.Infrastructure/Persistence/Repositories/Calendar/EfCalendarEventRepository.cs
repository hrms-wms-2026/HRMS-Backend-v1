using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventRepository : ICalendarEventRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents.AddAsync(calendarEvent, ct);

    public async Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && !e.IsRecurrenceCancelled
                        && (e.RecurrenceParentId != null || e.Recurrence == CalendarRecurrences.None)
                        && e.StartDate <= to && e.EndDate >= from
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.Recurrence != CalendarRecurrences.None
                        && e.RecurrenceParentId == null
                        && e.StartDate <= to
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents
            .Where(e => e.TenantId == tenantId && e.RecurrenceParentId == masterId)
            .ToListAsync(ct);

    public async Task<CalendarEvent?> GetTrackedChildByOriginalStartAsync(
        Guid tenantId, Guid masterId, DateTimeOffset originalStart, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents.FirstOrDefaultAsync(
            e => e.TenantId == tenantId && e.RecurrenceParentId == masterId && e.RecurrenceOriginalStart == originalStart, ct);

    public async Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AddRangeAsync(participants, ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipant>>> GetParticipantsForEventsAsync(
        Guid tenantId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default)
    {
        var rows = await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && eventIds.Contains(p.EventId))
            .ToListAsync(ct);
        return rows.GroupBy(p => p.EventId).ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventParticipant>)g.ToList());
    }

    public async Task<CalendarEventParticipant?> GetTrackedParticipantAsync(
        Guid tenantId, Guid eventId, Guid employeeId, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.EventId == eventId && p.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // CalendarEvent.CreatedById is a User id (set by AuditableEntityInterceptor from
        // ICurrentUser.UserId), not an Employee id - it cannot be compared to `employeeId`
        // directly. Resolve the employee's own User id first via a correlated subquery.
        var ownerUserId = _db.Employees.Where(emp => emp.TenantId == tenantId && emp.Id == employeeId).Select(emp => emp.UserId);

        return await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && !e.IsRecurrenceCancelled
                        && (e.RecurrenceParentId != null || e.Recurrence == CalendarRecurrences.None)
                        && e.StartDate <= to && e.EndDate >= from
                        // Owner OR participant - matches GetInDateRangeForCallerAsync's pattern.
                        // A participant-only check misses two real cases: a synced external event
                        // (CreatedById = the connecting user, never given a participant row) and a
                        // participant-less personal block ("Busy 2-3pm", no invitees). Both are
                        // real time on this employee's calendar and must count as a conflict when
                        // someone else checks their availability.
                        && (ownerUserId.Contains(e.CreatedById)
                            || _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId)))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset to, CancellationToken ct = default)
    {
        var ownerUserId = _db.Employees.Where(emp => emp.TenantId == tenantId && emp.Id == employeeId).Select(emp => emp.UserId);

        return await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.Recurrence != CalendarRecurrences.None
                        && e.RecurrenceParentId == null
                        && e.StartDate <= to
                        && (ownerUserId.Contains(e.CreatedById)
                            || _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId)))
            .ToListAsync(ct);
    }

    public void Update(CalendarEvent calendarEvent) => _db.PersonalCalendarEvents.Update(calendarEvent);
    public void Remove(CalendarEvent calendarEvent) => _db.PersonalCalendarEvents.Remove(calendarEvent);

    public async Task<IReadOnlyList<CalendarEvent>> GetManualEventsUpdatedSinceForUserAsync(
        Guid tenantId, Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedById == userId
                        && e.SourceType == CalendarEventSourceTypes.Manual
                        && e.Recurrence == CalendarRecurrences.None
                        && (e.UpdatedAt ?? e.CreatedAt) > since)
            // Ordered ascending so a caller that batches this result (CalendarSyncService.PushAsync)
            // processes oldest-changed-first and can derive a safe watermark from the last item in
            // whatever batch it actually pushes, instead of an arbitrary unordered cutoff.
            .OrderBy(e => e.UpdatedAt ?? e.CreatedAt)
            .ToListAsync(ct);

    public async Task RemoveHolidayEventsForYearAsync(Guid tenantId, int year, CancellationToken ct = default)
        => await _db.PersonalCalendarEvents
            .Where(e => e.TenantId == tenantId
                        && e.SourceType == CalendarEventSourceTypes.Holiday
                        && e.ExternalSource == CalendarExternalSources.CountryHoliday
                        && e.StartDate.Year == year)
            .ExecuteDeleteAsync(ct);
}
