using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfCalendarEventActivityLogRepository : ICalendarEventActivityLogRepository
{
    private readonly ApplicationDbContext _db;
    public EfCalendarEventActivityLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEventActivityLog log, CancellationToken ct = default)
        => await _db.CalendarEventActivityLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<CalendarEventActivityLog>> ListByEventIdAsync(Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventActivityLogs.AsNoTracking()
            .Where(l => l.CalendarEventId == calendarEventId)
            .OrderByDescending(l => l.PerformedAt)
            .ToListAsync(ct);
}
