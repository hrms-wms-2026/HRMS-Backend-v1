using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public sealed class EfCalendarIncidentRepository(ApplicationDbContext db) : ICalendarIncidentRepository
{
    public Task<List<CalendarMeetingIncident>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, CancellationToken ct) =>
        db.CalendarMeetingIncidents
            .Where(i => i.TenantId == tenantId && i.EmployeeId == employeeId && i.OccurrenceStart >= from)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarMeetingIncident>> ListForExceptionAsync(Guid tenantId, Guid exceptionId, CancellationToken ct) =>
        await db.CalendarMeetingIncidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ExceptionId == exceptionId)
            .OrderBy(i => i.OccurrenceStart)
            .ToListAsync(ct);

    public async Task<int> PurgeUnattachedAsync(Guid tenantId, DateTimeOffset before, CancellationToken ct)
    {
        var stale = await db.CalendarMeetingIncidents
            .Where(i => i.TenantId == tenantId && i.ExceptionId == null && i.OccurrenceStart < before)
            .ToListAsync(ct);
        db.CalendarMeetingIncidents.RemoveRange(stale);
        return stale.Count;
    }

    public void Add(CalendarMeetingIncident incident) => db.CalendarMeetingIncidents.Add(incident);

    public void Remove(CalendarMeetingIncident incident) => db.CalendarMeetingIncidents.Remove(incident);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
