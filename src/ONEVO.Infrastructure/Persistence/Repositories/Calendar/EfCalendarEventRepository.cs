using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventRepository : ICalendarEventRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
        => await _db.CalendarEvents.AddAsync(calendarEvent, ct);

    public async Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.CalendarEvents.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.CalendarEvents.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
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
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.Recurrence != CalendarRecurrences.None
                        && e.RecurrenceParentId == null
                        && e.StartDate <= to
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default)
        => await _db.CalendarEvents
            .Where(e => e.TenantId == tenantId && e.RecurrenceParentId == masterId)
            .ToListAsync(ct);

    public async Task<CalendarEvent?> GetTrackedChildByOriginalStartAsync(
        Guid tenantId, Guid masterId, DateTimeOffset originalStart, CancellationToken ct = default)
        => await _db.CalendarEvents.FirstOrDefaultAsync(
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

    public void Update(CalendarEvent calendarEvent) => _db.CalendarEvents.Update(calendarEvent);
    public void Remove(CalendarEvent calendarEvent) => _db.CalendarEvents.Remove(calendarEvent);
}
