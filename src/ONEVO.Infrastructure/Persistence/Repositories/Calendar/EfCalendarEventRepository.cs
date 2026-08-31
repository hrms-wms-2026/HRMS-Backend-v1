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
                        && e.StartDate <= to && e.EndDate >= from
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AddRangeAsync(participants, ct);

    public void Update(CalendarEvent calendarEvent) => _db.CalendarEvents.Update(calendarEvent);
    public void Remove(CalendarEvent calendarEvent) => _db.CalendarEvents.Remove(calendarEvent);
}
